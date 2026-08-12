$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Assert-Match([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text -notmatch $Pattern) { throw $Message }
}

function Assert-NoMatch([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text -match $Pattern) { throw $Message }
}

$manifest = Get-Content (Join-Path $repoRoot "azure.yaml") -Raw
$dockerfile = Get-Content (Join-Path $repoRoot "deploy\foundry\Dockerfile") -Raw
$preflight = Get-Content (Join-Path $repoRoot "scripts\foundry-preflight.ps1") -Raw

Assert-Match $manifest '(?m)^\s*host:\s*azure\.ai\.project\s*$' "Project dependency is missing."
Assert-Match $manifest '(?m)^\s*endpoint:\s*\$\{AZURE_AI_PROJECT_ENDPOINT\}\s*$' "Project endpoint is not deferred."
Assert-Match $manifest '(?m)^\s*host:\s*azure\.ai\.agent\s*$' "Hosted agent service is missing."
Assert-Match $manifest '(?s)protocol:\s*invocations\s+version:\s*2\.0\.0' "Invocations 2.0.0 is missing."
Assert-Match $manifest '(?m)^\s*provider:\s*microsoft\.foundry\s*$' "Foundry infra provider is missing."
Assert-Match $manifest '(?m)^\s*value:\s*\$\{AZURE_AI_MODEL_DEPLOYMENT_NAME\}\s*$' "Model setting is not deferred."
Assert-Match $manifest '(?m)^\s*project:\s*\.\s*$' "Agent project must be the repository root."
Assert-Match $manifest '(?s)docker:\s+context:\s*\.\s+path:\s*deploy/foundry/Dockerfile' "Docker context/path must resolve from the repository root."
Assert-Match $manifest '(?ms)^hooks:\s+preprovision:\s+shell:\s*pwsh\s+run:\s*\./scripts/foundry-preflight\.ps1 -Phase Provision\s+continueOnError:\s*false\s+predeploy:\s+shell:\s*pwsh\s+run:\s*\./scripts/foundry-preflight\.ps1 -Phase Deploy\s+continueOnError:\s*false' "Fail-closed project-level Foundry hooks are missing or malformed."
Assert-NoMatch $manifest 'AZURE_DEV_USER_AGENT' "The local azd user agent must not be persisted."
Assert-NoMatch $manifest '(?i)(subscriptionId|location):' "Subscription and region must not be committed."
Assert-NoMatch $manifest '(?m)^\s*endpoint:\s*https://' "A literal endpoint must not be committed."

Assert-Match $dockerfile '(?m)^FROM .+ AS .+$' "Production image must be multi-stage."
Assert-Match $dockerfile '(?m)^FROM mcr\.microsoft\.com/dotnet/aspnet:10\.0\.4-noble AS runtime\s*$' "Runtime must use the verified pinned .NET 10.0.4 noble image."
Assert-NoMatch $dockerfile '10\.0\.4-bookworm-slim' "The nonexistent .NET runtime tag must not return."
Assert-Match $dockerfile 'npm ci --ignore-scripts' "Pinned npm lockfile install is missing."
Assert-Match $dockerfile 'dotnet publish src/GovernedAgent\.Host/GovernedAgent\.Host\.csproj' "Host publish is missing."
Assert-NoMatch $dockerfile '(?m)^\s*RUN\s+.*(?:dotnet|npm)\s+test' "Tests must not run during the production image build."
Assert-Match $dockerfile 'COPILOT_AUTO_UPDATE=false' "Copilot auto-update is not disabled."
Assert-Match $dockerfile 'chmod -R a-w /opt/copilot /app/Hosted/verifier' "Runtime assets are not read-only."
Assert-Match $dockerfile '(?m)^USER app\s*$' "Production image must run as non-root."
Assert-Match $dockerfile '(?m)^EXPOSE 8088\s*$' "Foundry port is not exposed."
Assert-Match $dockerfile 'ENTRYPOINT \["dotnet", "GovernedAgent\.Host\.dll"\]' "Host entry point is incorrect."

foreach ($choice in @(
    "FOUNDRY_PROJECT_MODE",
    "AZURE_AI_PROJECT_ENDPOINT",
    "AZURE_SUBSCRIPTION_ID",
    "AZURE_LOCATION",
    "AZURE_AI_MODEL_DEPLOYMENT_NAME",
    "FOUNDRY_NETWORK_POSTURE",
    "COPILOT_AUTH_STRATEGY",
    "COPILOT_AUTH_REVIEWED",
    "APPROVAL_STORE_PROVIDER"
)) {
    Assert-Match $preflight ([regex]::Escape($choice)) "Preflight does not gate $choice."
}
Assert-Match $preflight '\[Parameter\(Mandatory\)\]' "Preflight Phase must be mandatory."
Assert-Match $preflight '\[ValidateSet\("Provision", "Deploy"\)\]' "Preflight phases must be restricted to Provision and Deploy."
Assert-Match $preflight '\$endpointRequired = \$Phase -eq "Deploy" -or \$projectMode -eq "existing"' "Endpoint phase semantics are missing."
Assert-Match $preflight 'if \(\$Phase -eq "Provision"\)' "Provision must have an independent success path."
Assert-Match $preflight 'COPILOT_AUTH_REVIEWED must be ''true''' "Deploy must require reviewed Copilot authentication."
Assert-Match $preflight 'provider-specific connectivity check' "Deploy must require approval-provider connectivity."
Assert-Match $preflight 'throw "Deployment blocked: implement and register the selected shared IApprovalStore' "Deploy must remain closed until the production approval store is implemented."

function Invoke-PreflightCase {
    param(
        [Parameter(Mandatory)][string]$Phase,
        [hashtable]$Values = @{},
        [Parameter(Mandatory)][int]$ExpectedExitCode,
        [string]$ExpectedOutput
    )

    $knownChoices = @(
        "FOUNDRY_PROJECT_MODE",
        "AZURE_AI_PROJECT_ENDPOINT",
        "AZURE_SUBSCRIPTION_ID",
        "AZURE_LOCATION",
        "AZURE_AI_MODEL_DEPLOYMENT_NAME",
        "FOUNDRY_NETWORK_POSTURE",
        "COPILOT_AUTH_STRATEGY",
        "COPILOT_AUTH_REVIEWED",
        "APPROVAL_STORE_PROVIDER"
    )
    $process = [System.Diagnostics.ProcessStartInfo]::new()
    $process.FileName = (Get-Process -Id $PID).Path
    $process.ArgumentList.Add("-NoProfile")
    $process.ArgumentList.Add("-File")
    $process.ArgumentList.Add((Join-Path $repoRoot "scripts\foundry-preflight.ps1"))
    $process.ArgumentList.Add("-Phase")
    $process.ArgumentList.Add($Phase)
    $process.UseShellExecute = $false
    $process.RedirectStandardOutput = $true
    $process.RedirectStandardError = $true
    foreach ($choice in $knownChoices) {
        [void]$process.Environment.Remove($choice)
    }
    foreach ($entry in $Values.GetEnumerator()) {
        $process.Environment[$entry.Key] = $entry.Value
    }

    $child = [System.Diagnostics.Process]::Start($process)
    $stdout = $child.StandardOutput.ReadToEnd()
    $stderr = $child.StandardError.ReadToEnd()
    $child.WaitForExit()
    $output = "$stdout`n$stderr"
    if ($child.ExitCode -ne $ExpectedExitCode) {
        throw "Preflight $Phase case exited $($child.ExitCode), expected $ExpectedExitCode. $output"
    }
    if ($ExpectedOutput -and $output -notmatch [regex]::Escape($ExpectedOutput)) {
        throw "Preflight $Phase case did not report '$ExpectedOutput'. $output"
    }
}

$common = @{
    FOUNDRY_PROJECT_MODE = "new"
    AZURE_SUBSCRIPTION_ID = "00000000-0000-0000-0000-000000000001"
    AZURE_LOCATION = "swedencentral"
    AZURE_AI_MODEL_DEPLOYMENT_NAME = "gpt-4.1"
    FOUNDRY_NETWORK_POSTURE = "private"
}
Invoke-PreflightCase -Phase Provision -Values $common -ExpectedExitCode 0 -ExpectedOutput "explicitly select a new project"

$existingWithoutEndpoint = $common.Clone()
$existingWithoutEndpoint.FOUNDRY_PROJECT_MODE = "existing"
Invoke-PreflightCase -Phase Provision -Values $existingWithoutEndpoint -ExpectedExitCode 1 -ExpectedOutput "AZURE_AI_PROJECT_ENDPOINT is not configured."

$deployChoices = $common.Clone()
$deployChoices.AZURE_AI_PROJECT_ENDPOINT = "https://account.services.ai.azure.com/api/projects/production"
$deployChoices.COPILOT_AUTH_STRATEGY = "managed-identity"
$deployChoices.COPILOT_AUTH_REVIEWED = "true"
$deployChoices.APPROVAL_STORE_PROVIDER = "azure-cosmos-db"
Invoke-PreflightCase -Phase Deploy -Values $deployChoices -ExpectedExitCode 1 -ExpectedOutput "Deployment blocked: implement and register the selected shared IApprovalStore"

Write-Host "Foundry scaffold validation passed."
