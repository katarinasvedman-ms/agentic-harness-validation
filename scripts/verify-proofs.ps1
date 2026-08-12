$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$latticeSource = Join-Path $repoRoot "src\plan-verifier\src\lattice.ts"
$latticeGenerated = Join-Path $repoRoot "src\plan-verifier\src\lattice.dfy.gen"
$latticeProof = Join-Path $repoRoot "src\plan-verifier\src\lattice.dfy"
$planProof = Join-Path $repoRoot "src\plan-verifier\proofs\plan_invariants.dfy"

npx lsc gen --backend=dafny $latticeSource
if ($LASTEXITCODE -ne 0) {
    throw "LemmaScript generation failed."
}

$generatedProof = (Get-Content $latticeGenerated -Raw).Replace("`r`n", "`n").TrimEnd("`n")
$checkedInProof = (Get-Content $latticeProof -Raw).Replace("`r`n", "`n").TrimEnd("`n")
if ($generatedProof -ne $checkedInProof) {
    throw "The checked-in LemmaScript proof is out of date with its generated model."
}

$solverPath = $env:Z3_PATH
if ([string]::IsNullOrWhiteSpace($solverPath)) {
    if (-not $IsWindows) {
        throw "Set Z3_PATH to a Z3 4.12.1 executable before verifying proofs."
    }

    $solverRoot = Join-Path $repoRoot ".tools\z3-4.12.1"
    $archivePath = Join-Path $solverRoot "z3-4.12.1-x64-win.zip"
    $solverPath = Join-Path $solverRoot "z3-4.12.1-x64-win\bin\z3.exe"
    if (-not (Test-Path $solverPath)) {
        New-Item -ItemType Directory -Force -Path $solverRoot | Out-Null
        Invoke-WebRequest `
            -Uri "https://github.com/Z3Prover/z3/releases/download/z3-4.12.1/z3-4.12.1-x64-win.zip" `
            -OutFile $archivePath
        $actualHash = (Get-FileHash $archivePath -Algorithm SHA256).Hash
        $expectedHash = "CE2D658D007C4F5873D2279BD031D4E72500B388E1EF2D716BD5F86AF19B20D2"
        if ($actualHash -ne $expectedHash) {
            throw "The downloaded Z3 archive did not match the pinned SHA-256 digest."
        }
        Expand-Archive -Force $archivePath $solverRoot
    }
}

if (-not (Test-Path $solverPath)) {
    throw "Z3 was not found at '$solverPath'."
}

$solverVersion = (& $solverPath --version)
if ($LASTEXITCODE -ne 0 -or $solverVersion -notmatch "4\.12\.1") {
    throw "Z3 4.12.1 is required, but '$solverVersion' was found."
}

dotnet tool run dafny -- verify `
    --solver-path $solverPath `
    $latticeProof `
    $planProof
if ($LASTEXITCODE -ne 0) {
    throw "Dafny proof verification failed."
}
