[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath,

    [string]$AgentStatusPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-RelativeDigest([string]$Path) {
    $resolved = Resolve-Path $Path
    [ordered]@{
        path = [IO.Path]::GetRelativePath($repoRoot, $resolved.Path).Replace("\", "/")
        sha256 = (Get-FileHash $resolved.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Find-Values($Value, [string[]]$Names) {
    $results = [Collections.Generic.List[object]]::new()
    if ($null -eq $Value) { return $results }
    if ($Value -is [Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            if ($Names -contains [string]$key -and $null -ne $Value[$key]) {
                $results.Add($Value[$key])
            }
            foreach ($nested in (Find-Values $Value[$key] $Names)) {
                $results.Add($nested)
            }
        }
    }
    elseif ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        foreach ($item in $Value) {
            foreach ($nested in (Find-Values $item $Names)) {
                $results.Add($nested)
            }
        }
    }
    return $results
}

$statusDocument = $null
if ($AgentStatusPath -and (Test-Path $AgentStatusPath -PathType Leaf)) {
    try {
        $statusDocument = Get-Content $AgentStatusPath -Raw |
            ConvertFrom-Json -AsHashtable -Depth 50
    }
    catch {
        throw "The azd agent status output was not valid JSON."
    }
}

$policyFiles = @(
    Get-ChildItem (Join-Path $repoRoot "src\GovernedAgent.Governance") `
        -Recurse -File -Filter "*Policy*.cs"
)
$proofFiles = @(
    Join-Path $repoRoot "src\plan-verifier\src\lattice.dfy"
    Join-Path $repoRoot "src\plan-verifier\proofs\plan_invariants.dfy"
)
$reportFiles = @(
    Join-Path $repoRoot "docs\GUARANTEE_REPORT.md"
    Join-Path $repoRoot "docs\VERIFICATION_SPEC.md"
)
$evalManifestPath = Join-Path $repoRoot "evals\suite-manifest.v1.json"
$evalManifest = Get-Content $evalManifestPath -Raw | ConvertFrom-Json

$image = [ordered]@{
    reference = $null
    id = $null
    digests = @()
    signature = $null
    signatureReason = "No signing provider is configured; no signature is claimed."
}
$imageReference = "governed-agent-host:release-candidate"
$imageJson = docker image inspect $imageReference 2>$null
if ($LASTEXITCODE -ne 0) {
    $imageReference = docker image ls --format "{{.Repository}}:{{.Tag}}" |
        Where-Object { $_ -match 'governed-agent-host' } |
        Select-Object -First 1
    if ($imageReference) {
        $imageJson = docker image inspect $imageReference 2>$null
    }
}
if ($LASTEXITCODE -eq 0 -and $imageJson) {
    $imageInfo = $imageJson | ConvertFrom-Json
    $image.reference = $imageReference
    $image.id = $imageInfo[0].Id
    $image.digests = @($imageInfo[0].RepoDigests | Where-Object { $_ })
}

$agent = [ordered]@{
    source = "azd ai agent show --output json"
    available = $null -ne $statusDocument
    name = $null
    version = $null
    status = $null
    endpoints = @()
}
if ($statusDocument) {
    $agent.name = @(Find-Values $statusDocument @("name", "agentName"))[0]
    $agent.version = @(Find-Values $statusDocument @("version", "agentVersion"))[0]
    $agent.status = @(Find-Values $statusDocument @("status", "provisioningState"))[0]
    $agent.endpoints = @(
        Find-Values $statusDocument @("endpoint", "endpointUrl", "url") |
            ForEach-Object { [string]$_ } |
            Where-Object { $_ -match '^https://' } |
            Sort-Object -Unique
    )
}

$provenance = [ordered]@{
    schemaVersion = "1.0"
    generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
    source = [ordered]@{
        commitSha = $env:GITHUB_SHA
        repository = $env:GITHUB_REPOSITORY
        workflowRunId = $env:GITHUB_RUN_ID
        workflowRunAttempt = $env:GITHUB_RUN_ATTEMPT
    }
    outcomes = [ordered]@{
        validationJob = $env:RELEASE_VALIDATION_OUTCOME
        deploymentJob = $env:RELEASE_DEPLOY_JOB_OUTCOME
        preflight = $env:RELEASE_PREFLIGHT_OUTCOME
        deploy = $env:RELEASE_DEPLOY_OUTCOME
        status = $env:RELEASE_STATUS_OUTCOME
        smoke = $env:RELEASE_SMOKE_OUTCOME
    }
    agent = $agent
    artifacts = [ordered]@{
        policies = @($policyFiles | ForEach-Object { Get-RelativeDigest $_.FullName })
        proofs = @($proofFiles | ForEach-Object { Get-RelativeDigest $_ })
        reports = @($reportFiles | ForEach-Object { Get-RelativeDigest $_ })
    }
    image = $image
    evaluationSuiteIntent = [ordered]@{
        path = "evals/suite-manifest.v1.json"
        sha256 = (Get-FileHash $evalManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        suiteVersion = $evalManifest.suiteVersion
        tiers = @(
            $evalManifest.tiers | ForEach-Object {
                [ordered]@{
                    id = $_.id
                    execution = $_.execution
                    blocking = $_.blocking
                    evaluators = @($_.evaluator, $_.evaluators | Where-Object { $_ })
                }
            }
        )
        note = "This records versioned evaluation intent, not model completion or a remote Foundry evaluation result."
    }
}

$parent = Split-Path -Parent $OutputPath
if ($parent) {
    New-Item -ItemType Directory -Force $parent | Out-Null
}
$provenance | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $OutputPath
Write-Host "Release provenance created without environment-value or invocation-response content."
