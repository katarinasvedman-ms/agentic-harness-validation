[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Generate", "Validate")]
    [string]$Mode,

    [Parameter(Mandatory)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

if ($Mode -eq "Generate") {
    $now = [DateTimeOffset]::UtcNow
    $payload = [ordered]@{
        action = "execute"
        plan = [ordered]@{
            schemaVersion = "1.0"
            planId = [Guid]::NewGuid()
            incidentId = "INC-1042"
            agentId = "incident-agent"
            deploymentVersion = "1.0.0"
            createdAt = $now.AddMinutes(-1).ToString("O")
            expiresAt = $now.AddMinutes(5).ToString("O")
            steps = @(
                [ordered]@{
                    stepId = "release-smoke-write"
                    capability = "service.restart"
                    tool = "restart_service"
                    resource = [ordered]@{
                        type = "service"
                        id = "payments-api"
                        environment = "production"
                        classification = "internal"
                    }
                    dataSources = @(
                        [ordered]@{
                            id = "payments-api-metrics"
                            classification = "internal"
                        }
                    )
                    destination = [ordered]@{
                        id = "payments-api"
                        classification = "internal-trusted"
                    }
                    arguments = [ordered]@{
                        serviceId = "payments-api"
                        instanceId = "payments-api-03"
                    }
                    dependsOn = @()
                    effect = "write"
                    approvalClass = "incident-commander"
                    compensation = [ordered]@{
                        tool = "restore_service_state"
                        arguments = [ordered]@{
                            serviceId = "payments-api"
                            instanceId = "payments-api-03"
                            previousHealth = "degraded"
                            sourceVersion = 1
                        }
                    }
                }
            )
        }
        stepId = "release-smoke-write"
        idempotencyKey = "release-smoke-$([Guid]::NewGuid().ToString('N'))"
        expectedResourceVersion = 1
        completionCriteria = [ordered]@{
            incidentId = "INC-1042"
            incidentStatus = $null
            serviceId = "payments-api"
            serviceHealth = "healthy"
        }
    }

    $parent = Split-Path -Parent $Path
    if ($parent) {
        New-Item -ItemType Directory -Force $parent | Out-Null
    }
    $payload | ConvertTo-Json -Depth 12 -Compress | Set-Content -Encoding utf8 $Path
    Write-Host "Generated a dynamic strict execute payload; identifiers and values omitted."
    return
}

if (-not (Test-Path $Path -PathType Leaf)) {
    throw "The smoke response file is missing."
}

$raw = Get-Content $Path -Raw
$jsonStart = $raw.IndexOf("{", [StringComparison]::Ordinal)
if ($jsonStart -lt 0) {
    throw "The smoke response did not contain a JSON body."
}

try {
    $response = $raw.Substring($jsonStart) | ConvertFrom-Json -Depth 30
}
catch {
    throw "The smoke response body was not valid structured JSON."
}

if ($response.workflow.status -ne "approval-required" -or
    $response.workflow.gatewayResult.outcome -ne "approval-required" -or
    [string]::IsNullOrWhiteSpace($response.resumeToken)) {
    throw "The smoke request did not stop at the required approval boundary."
}

$state = $response.workflow.simulatorState
if ($null -eq $state -or
    $state.isComplete -ne $false -or
    $state.serviceHealth -ne "degraded" -or
    [long]$state.serviceVersion -ne 1) {
    throw "The approval-required smoke request changed simulator state."
}

if ($response.workflow.status -eq "completed") {
    throw "Model or protocol completion cannot stand in for business completion."
}

Write-Host "Smoke response proved approvalRequired with unchanged simulator state."
