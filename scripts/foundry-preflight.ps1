[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Provision", "Deploy")]
    [string]$Phase
)

$ErrorActionPreference = "Stop"

function Read-Choice([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Name is not configured."
    }

    if ($value -match '(?i)(<.+>|changeme|placeholder|example|todo)') {
        throw "$Name contains a placeholder."
    }

    return $value.Trim()
}

$projectMode = Read-Choice "FOUNDRY_PROJECT_MODE"
if ($projectMode -notin @("existing", "new")) {
    throw "FOUNDRY_PROJECT_MODE must be 'existing' or 'new'."
}

$subscriptionId = Read-Choice "AZURE_SUBSCRIPTION_ID"
if ($subscriptionId -notmatch '^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') {
    throw "AZURE_SUBSCRIPTION_ID must be a subscription GUID."
}

$location = Read-Choice "AZURE_LOCATION"
if ($location -notmatch '^[a-z0-9]+$') {
    throw "AZURE_LOCATION must be an Azure region name."
}

$modelDeployment = Read-Choice "AZURE_AI_MODEL_DEPLOYMENT_NAME"
if ($modelDeployment -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
    throw "AZURE_AI_MODEL_DEPLOYMENT_NAME is not a valid deployment name."
}

$networkPosture = Read-Choice "FOUNDRY_NETWORK_POSTURE"
if ($networkPosture -notin @("public", "selected-networks", "private")) {
    throw "FOUNDRY_NETWORK_POSTURE must be public, selected-networks, or private."
}

$projectEndpoint = [Environment]::GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
$endpointRequired = $Phase -eq "Deploy" -or $projectMode -eq "existing"
if ($endpointRequired) {
    $projectEndpoint = Read-Choice "AZURE_AI_PROJECT_ENDPOINT"
}
if (-not [string]::IsNullOrWhiteSpace($projectEndpoint) -and
    $projectEndpoint.Trim() -notmatch '^https://[^/]+/api/projects/[^/]+/?$') {
    throw "AZURE_AI_PROJECT_ENDPOINT must be a Foundry project endpoint."
}

if ($Phase -eq "Provision") {
    if ($projectMode -eq "new") {
        Write-Host "Foundry provision choices explicitly select a new project (values intentionally omitted)."
    }
    else {
        Write-Host "Foundry provision choices select an existing project (values intentionally omitted)."
    }
    return
}

$copilotAuthStrategy = Read-Choice "COPILOT_AUTH_STRATEGY"
if ($copilotAuthStrategy -match '(?i)(interactive|development|test|placeholder|none)') {
    throw "COPILOT_AUTH_STRATEGY must identify a reviewed non-interactive production strategy."
}
if ((Read-Choice "COPILOT_AUTH_REVIEWED") -ne "true") {
    throw "COPILOT_AUTH_REVIEWED must be 'true' after production authentication review."
}

$approvalProvider = Read-Choice "APPROVAL_STORE_PROVIDER"
if ($approvalProvider -match '(?i)(in-?memory|local|development|test|none)') {
    throw "APPROVAL_STORE_PROVIDER must identify a production shared IApprovalStore provider."
}

Write-Host "Foundry deploy choices are configured (values intentionally omitted)."
throw "Deployment blocked: implement and register the selected shared IApprovalStore and its provider-specific connectivity check before production deployment."
