$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $repoRoot ".github\workflows\deploy.yml"
$workflow = Get-Content $workflowPath -Raw
$smokeScript = Get-Content (Join-Path $repoRoot "scripts\hosted-agent-smoke.ps1") -Raw

function Assert-Match([string]$Pattern, [string]$Message) {
    if ($workflow -notmatch $Pattern) { throw $Message }
}

function Assert-NoMatch([string]$Pattern, [string]$Message) {
    if ($workflow -match $Pattern) { throw $Message }
}

Assert-Match '(?ms)^on:\s+workflow_dispatch:\s*$' "Release workflow must be dispatch-only."
Assert-NoMatch '(?m)^\s+(push|pull_request|schedule):' "Release workflow has an automatic trigger."
Assert-Match '(?ms)^permissions:\s+contents:\s*read\s+id-token:\s*write\s*$' "OIDC permissions are not minimal."
Assert-Match '(?ms)^concurrency:\s+group:\s*hosted-agent-demo\s+cancel-in-progress:\s*false' "Deployment concurrency is missing."
Assert-Match '(?s)deploy-demo:.*needs:\s*release-validation.*environment:\s+name:\s*demo' "The deployment is not validation-gated by the demo environment."
Assert-Match 'FOUNDRY_PROJECT_MODE:\s*existing' "The first CD iteration must target an existing project."
Assert-NoMatch '(?m)^\s*azd\s+(provision|up)\b' "The release workflow must not provision Azure resources."
Assert-Match '\./scripts/foundry-preflight\.ps1 -Phase Deploy' "The deploy preflight cannot be bypassed."
Assert-Match 'azd deploy governed-agent-host --no-prompt' "The governed hosted agent deployment is missing."
Assert-NoMatch '\$\{\{\s*secrets\.' "Static GitHub secrets must not be used for Azure authentication or configuration."
if ($smokeScript -notmatch '\$response\.workflow\.status -ne "approval-required"' -or
    $smokeScript -notmatch '\$state\.serviceHealth -ne "degraded"') {
    throw "The structured approvalRequired/no-side-effect smoke assertions are missing."
}

$pins = @(
    "actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683",
    "actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9",
    "actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020",
    "Azure/setup-azd@0b7e3a35ab00f2eee7080c845eb39c3f0ebfa553",
    "azure/login@7184910d9eb2b1c5e48f7073824a90609bb9b6d6",
    "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02"
)
foreach ($pin in $pins) {
    Assert-Match ([regex]::Escape($pin)) "Required action pin '$pin' is missing."
}

$actionReferences = [regex]::Matches($workflow, '(?m)^\s*uses:\s*([^\s#]+)') |
    ForEach-Object { $_.Groups[1].Value }
foreach ($reference in $actionReferences) {
    if ($reference -notmatch '@[0-9a-f]{40}$') {
        throw "Action reference '$reference' is not SHA-pinned."
    }

    Assert-Match '(?s)record-run:.*if:\s*\$\{\{\s*always\(\)\s*\}\}.*needs:\s+- release-validation\s+- deploy-demo' "Run-level provenance must execute after every release attempt."
    Assert-Match 'RELEASE_VALIDATION_OUTCOME:\s*\$\{\{\s*needs\.release-validation\.result\s*\}\}' "Validation outcome is missing from provenance."
    Assert-Match 'RELEASE_DEPLOY_JOB_OUTCOME:\s*\$\{\{\s*needs\.deploy-demo\.result\s*\}\}' "Deployment outcome is missing from provenance."
}

$azdSteps = [regex]::Matches(
    $workflow,
    '(?ms)^\s{6}- name: .+?\n(?=\s{6}- name: |\z)') |
    Where-Object { $_.Value -match '(?m)^\s+azd\s' }
foreach ($step in $azdSteps) {
    if ($step.Value -notmatch '\$env:AZURE_DEV_USER_AGENT = "microsoft_foundry_skill"') {
        throw "Every azd command step must set the Foundry user agent locally."
    }
}

Write-Host "Release workflow static validation passed."
