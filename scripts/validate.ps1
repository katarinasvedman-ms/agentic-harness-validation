$ErrorActionPreference = "Stop"

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
