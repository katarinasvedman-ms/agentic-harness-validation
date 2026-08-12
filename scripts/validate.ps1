$ErrorActionPreference = "Stop"

& "$PSScriptRoot\validate-foundry-scaffold.ps1"
if (-not $?) { throw "The Foundry deployment scaffold is invalid." }

dotnet build GovernedAgentDemo.sln --configuration Release
if ($LASTEXITCODE -ne 0) { throw "The .NET build failed." }

dotnet test GovernedAgentDemo.sln --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "The .NET tests failed." }

npm run lint
if ($LASTEXITCODE -ne 0) { throw "The frontend or verifier lint failed." }

npm run build
if ($LASTEXITCODE -ne 0) { throw "The frontend or verifier build failed." }

npm test
if ($LASTEXITCODE -ne 0) { throw "The verifier tests failed." }

& "$PSScriptRoot\verify-proofs.ps1"
if ($LASTEXITCODE -ne 0) { throw "The formal proofs failed." }

& "$PSScriptRoot\generate-guarantee-report.ps1" -Check
if ($LASTEXITCODE -ne 0) { throw "The guarantee report is stale." }
