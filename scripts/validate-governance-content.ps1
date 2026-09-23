$ErrorActionPreference = "Stop"

$docsRoot = Join-Path (Split-Path $PSScriptRoot -Parent) "docs"
$presentationPath = Join-Path $docsRoot "agent-governance-presentation.html"
$pitchPath = Join-Path $docsRoot "pitch.html"
$presentation = Get-Content $presentationPath -Raw
$pitch = Get-Content $pitchPath -Raw

function Assert-Matches {
    param(
        [string]$Content,
        [string]$Pattern,
        [string]$Message
    )

    if ($Content -notmatch $Pattern) {
        throw $Message
    }
}

function Assert-DoesNotMatch {
    param(
        [string]$Content,
        [string]$Pattern,
        [string]$Message
    )

    if ($Content -match $Pattern) {
        throw $Message
    }
}

Assert-Matches $presentation 'href="pitch\.html"' `
    "The governance presentation must link to the concrete pitch."
Assert-Matches $pitch 'href="agent-governance-presentation\.html(?:#[^"]+)?"' `
    "The pitch must link to the portfolio governance presentation."

foreach ($question in @(
    "Where are my agents?",
    "What can they do?",
    "What are they doing?",
    "How do I respond?"
)) {
    Assert-Matches $presentation ([regex]::Escape($question)) `
        "The presentation is missing the Blueprint bridge question: $question"
}

Assert-Matches $presentation 'does not imply Alliance membership or endorsement' `
    "The Blueprint bridge must explicitly disclaim Alliance membership and endorsement."
Assert-Matches $presentation 'Response &amp; Recovery|Response & Recovery' `
    "The presentation must include an explicit Response & Recovery pillar."
Assert-Matches $presentation 're-attestation' `
    "The presentation must describe re-attestation."
Assert-Matches $presentation 'read-only' `
    "The presentation must describe staged read-only recovery."

Assert-Matches $pitch 'structured plan and trusted action envelope, not the raw user prompt' `
    "The pitch must explain why authorization evaluates the structured plan rather than the raw prompt."
Assert-Matches $pitch 'Tier 3 agent' `
    "The pitch scenario must map to Tier 3."
Assert-Matches $pitch 'Third-party, tech preview, non-Microsoft' `
    "LemmaScript must be labelled third-party, preview, and non-Microsoft."
Assert-Matches $pitch 'Academic and open-source research, non-Microsoft' `
    "Verus must be labelled academic/research and non-Microsoft."
Assert-Matches $pitch 'Microsoft Research origin' `
    "Dafny must be identified as originating from Microsoft Research."
Assert-Matches $pitch 'not a recommended production bill of materials' `
    "The technology table must include the frontier-stack disclaimer."
Assert-Matches $pitch 'restore read-only' `
    "The pitch incident path must include staged recovery."

$governanceDocs = Get-ChildItem $docsRoot -File |
    Where-Object { $_.Extension -in ".html", ".md" }
$forbiddenClaims = @(
    '\bglobal kill switch\b',
    '\buniversal kill switch\b',
    '\ball execution stopped\b',
    '\b(?:Microsoft|OpenAI|Anthropic)\b.{0,80}\bfounding member\b',
    '\bfounding member\b.{0,80}\b(?:Microsoft|OpenAI|Anthropic)\b'
)

foreach ($document in $governanceDocs) {
    $content = Get-Content $document.FullName -Raw
    foreach ($claim in $forbiddenClaims) {
        Assert-DoesNotMatch $content $claim `
            "Unsupported governance claim '$claim' found in $($document.Name)."
    }
}

Write-Host "Governance content validation passed."
