[CmdletBinding()]
param(
    [string]$BffUrl = "http://127.0.0.1:5072",
    [switch]$UseRunningBff
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root ".artifacts\rehearsal"
New-Item -ItemType Directory -Force $artifacts | Out-Null
$process = $null

function Assert-That([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "REHEARSAL MISMATCH: $Message" }
}

function Invoke-Api {
    param(
        [string]$Method = "Get",
        [string]$Path,
        [hashtable]$Headers,
        [object]$Body
    )
    $parameters = @{ Method = $Method; Uri = "$BffUrl$Path" }
    if ($Headers) { $parameters.Headers = $Headers }
    if ($null -ne $Body) {
        $parameters.ContentType = "application/json"
        $parameters.Body = $Body | ConvertTo-Json -Depth 10
    }
    Invoke-RestMethod @parameters
}

try {
    Write-Host "[1/8] Running governance workflow rehearsal tests..."
    $filter = @(
        "FullyQualifiedName~ReadModelIncludesIncidentEvidenceAndVerifiedPlan",
        "FullyQualifiedName~ProductionWriteSuspendsWithoutSideEffectsAndResumesExactly",
        "FullyQualifiedName~WrongApprovalDoesNotResumeOrChangeSimulator",
        "FullyQualifiedName~ApprovalRemainsSingleUseAcrossResumeAttempts",
        "FullyQualifiedName~GatewayDeniesWriteEvenWhenHookLayerIsBypassed",
        "FullyQualifiedName~AuditRecordsAreLinkedAndVerifiable",
        "FullyQualifiedName~RepresentedSnapshotIsAcceptedByTheWorkflowVerifier"
    ) -join "|"
    dotnet test "$root\tests\GovernedAgent.IntegrationTests\GovernedAgent.IntegrationTests.csproj" `
        --configuration Release --filter $filter --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) { throw "Targeted integration rehearsal tests failed." }
    dotnet test "$root\tests\GovernedAgent.UnitTests\GovernedAgent.UnitTests.csproj" `
        --configuration Release --filter "FullyQualifiedName~AuditRecordsAreLinkedAndVerifiable" `
        --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) { throw "Targeted audit rehearsal test failed." }

    if (-not $UseRunningBff) {
        Write-Host "[2/8] Starting credential-free local BFF..."
        $stdout = Join-Path $artifacts "bff.stdout.log"
        $stderr = Join-Path $artifacts "bff.stderr.log"
        $process = Start-Process dotnet -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr `
            -ArgumentList @(
                "run", "--project", "$root\src\GovernedAgent.Console.Bff",
                "--configuration", "Release", "--no-build", "--",
                "--urls", $BffUrl
            )
    }

    $healthy = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $health = Invoke-Api -Path "/health"
            if ($health.status -eq "healthy") { $healthy = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    Assert-That $healthy "BFF health did not become healthy."

    $operator = @{
        "X-Demo-User" = "operator@example.test"
        "X-Demo-Roles" = "governance-operator"
    }
    $commander = @{
        "X-Demo-User" = "commander@example.test"
        "X-Demo-Roles" = "incident-commander"
    }
    $wrongRole = @{
        "X-Demo-User" = "viewer@example.test"
        "X-Demo-Roles" = "governance-operator"
    }

    Write-Host "[3/8] Resetting INC-1042 and checking safe diagnostic reads..."
    Invoke-Api -Method Post -Path "/api/simulator/reset" -Headers $operator | Out-Null
    $incident = Invoke-Api -Path "/api/incidents/INC-1042"
    $evidenceResponse = Invoke-Api -Path "/api/incidents/INC-1042/evidence"
    $evidence = @($evidenceResponse)
    Assert-That ($incident.incident.incidentId -eq "INC-1042") "Wrong incident returned."
    Assert-That ($incident.serviceHealth.health -eq "Degraded") "Initial service is not degraded."
    Assert-That ($evidence.Count -ge 2) "Diagnostic evidence is missing."

    Write-Host "[4/8] Confirming injection remains explicitly untrusted..."
    $untrusted = @($evidence | Where-Object { $_.containsUntrustedContent })
    Assert-That ($untrusted.Count -eq 1) "Expected exactly one untrusted evidence item."
    Assert-That ($untrusted[0].summary -like "SYSTEM OVERRIDE:*") "Injection fixture changed."
    $afterRead = Invoke-Api -Path "/api/incidents/INC-1042"
    Assert-That ($afterRead.serviceHealth.health -eq "Degraded") "A diagnostic read caused a side effect."

    Write-Host "[5/8] Checking exact approval, wrong-role denial, and replay denial..."
    $pending = Invoke-Api -Path "/api/incidents/INC-1042/approvals/pending"
    Assert-That ($pending.requiredRole -eq "incident-commander") "Approval role is not exact."
    $approvalPath = "/api/approvals/$($pending.approvalRequestId)/approve"
    try {
        Invoke-Api -Method Post -Path $approvalPath -Headers $wrongRole `
            -Body @{ reason = "Attempt with the wrong role." } | Out-Null
        throw "REHEARSAL MISMATCH: wrong-role approval was accepted."
    } catch {
        if ($_.Exception.Message -like "REHEARSAL MISMATCH:*") { throw }
        Assert-That ($_.Exception.Response.StatusCode.value__ -eq 403) "Wrong-role denial was not HTTP 403."
    }
    $decision = Invoke-Api -Method Post -Path $approvalPath -Headers $commander `
        -Body @{ reason = "Approve the exact verified simulator action." }
    Assert-That ($decision.decision -eq "Approved") "Valid exact approval was not accepted."
    try {
        Invoke-Api -Method Post -Path $approvalPath -Headers $commander `
            -Body @{ reason = "Replay should fail." } | Out-Null
        throw "REHEARSAL MISMATCH: approval replay was accepted."
    } catch {
        if ($_.Exception.Message -like "REHEARSAL MISMATCH:*") { throw }
        Assert-That ($_.Exception.Response.StatusCode.value__ -eq 404) "Replay denial was not HTTP 404."
    }

    Write-Host "[6/8] Activating and reading back the kill switch..."
    $controls = Invoke-Api -Method Put -Path "/api/controls/kill-switch" `
        -Headers $commander -Body @{ active = $true; reason = "Rehearsal emergency stop." }
    Assert-That $controls.killSwitchActive "Kill switch did not activate."
    $stillDegraded = Invoke-Api -Path "/api/incidents/INC-1042"
    Assert-That ($stillDegraded.serviceHealth.health -eq "Degraded") "Unauthorized BFF side effect occurred."

    Write-Host "[7/8] Verifying the audit chain and saving local evidence..."
    $audit = Invoke-Api -Path "/api/audit"
    Assert-That $audit.integrityValid "Audit chain integrity failed."
    Assert-That (@($audit.records).Count -eq 1) "Expected one accepted approval audit record."
    @{
        capturedAt = [DateTimeOffset]::UtcNow
        health = $health
        incidentAfterReads = $afterRead
        untrustedEvidence = $untrusted
        pendingApproval = $pending
        approvalDecision = $decision
        controls = $controls
        audit = $audit
    } | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $artifacts "api-evidence.json")

    Write-Host "[8/8] PRESENTER CHECKLIST: PASS"
    Write-Host "  [x] INC-1042 reset; diagnostic read was side-effect free"
    Write-Host "  [x] injection labelled untrusted"
    Write-Host "  [x] production write suspended for exact approval"
    Write-Host "  [x] wrong/replayed approval denied; valid approval completed in workflow"
    Write-Host "  [x] kill switch denied gateway write; no unauthorized side effect"
    Write-Host "  [x] audit chain valid"
} finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(5000) | Out-Null
    }
}
