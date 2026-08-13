# Local operations runbook

## Scope and prerequisites

This runbook operates only the credential-free local simulator, console, BFF,
host harness, verifier, and tests. PowerShell 7, .NET 10, Node.js 22+, and
restored repository dependencies are required. Azure provisioning and teardown
are explicitly deferred; see `docs/RELEASE_DEPLOYMENT.md`.

## Start, health, and stop

```powershell
# BFF (live API, in-memory state)
dotnet run --project .\src\GovernedAgent.Console.Bff -- --urls http://127.0.0.1:5072

# console (separate terminal, static deterministic presentation)
npm run dev --workspace governedagent-console -- --host 127.0.0.1

# optional hosted harness (separate terminal)
dotnet run --project .\src\GovernedAgent.Host -- --urls http://127.0.0.1:8088
```

Stop foreground processes with Ctrl+C. Do not kill by process name. If
automation started a process, retain its PID and use `Stop-Process -Id <pid>`.

```powershell
irm http://127.0.0.1:5072/health       # BFF liveness; local readiness is the same
irm http://127.0.0.1:8088/health       # host liveness
irm http://127.0.0.1:8088/readiness    # host dependency readiness
```

The BFF has no separate readiness endpoint. A 200 `/health` plus a successful
`/api/incidents/INC-1042` read is the local readiness check.

## Reset and simulator state

All BFF state is process-local and volatile. Reset restores `INC-1042`,
degraded `payments-api-03`, version counters, pending approval, and kill switch.
It does not erase the in-memory audit chain.

```powershell
$headers = @{
  "X-Demo-User" = "operator@example.test"
  "X-Demo-Roles" = "governance-operator"
}
irm -Method Post -Headers $headers http://127.0.0.1:5072/api/simulator/reset
irm http://127.0.0.1:5072/api/incidents/INC-1042
irm http://127.0.0.1:5072/api/incidents/INC-1042/evidence
```

A full clean state, including audit, requires stopping and restarting the BFF.

## Kill switch and approvals

The local headers are a demo identity adapter, **not authentication**.

```powershell
$commander = @{
  "X-Demo-User" = "commander@example.test"
  "X-Demo-Roles" = "incident-commander"
}
$body = @{ active = $true; reason = "Presenter emergency-stop rehearsal." } |
  ConvertTo-Json
irm -Method Put -Headers $commander -ContentType application/json -Body $body `
  http://127.0.0.1:5072/api/controls/kill-switch
irm http://127.0.0.1:5072/api/controls
```

Reset deactivates the switch. Approval requires the exact pending request ID,
the exact lowercase `incident-commander` role, a nonblank reason, and an
unexpired request. A decision removes the pending request; approval artifacts
are digest-bound and single-use. Use the rehearsal script instead of manually
copying nonces when demonstrating execution semantics.

## Audit and evidence collection

```powershell
New-Item -ItemType Directory -Force .\.artifacts\rehearsal | Out-Null
irm http://127.0.0.1:5072/api/audit |
  ConvertTo-Json -Depth 20 |
  Set-Content .\.artifacts\rehearsal\audit.json
pwsh .\scripts\rehearse-local-demo.ps1
```

Confirm `integrityValid: true`. The local audit is tamper-evident in memory, not
durable, immutable storage. Evidence files can contain simulator attack text
and identifiers; review before sharing. They contain no intended credentials.

## Troubleshooting

| Symptom | Check / action |
| --- | --- |
| Port already in use | Stop the known PID or choose another `--urls` value; pass the matching `-BffUrl` to rehearsal. |
| BFF never becomes healthy | Read `.artifacts\rehearsal\bff.stdout.log` and `bff.stderr.log`; run `dotnet build GovernedAgentDemo.sln`. |
| 401/403 mutation | Supply exactly one safe `X-Demo-User` and the exact lowercase required role. |
| 404 approval | Reset; the request was absent, expired, decided, or replayed. |
| Verification failure | Run `npm run build --workspace @governed-agent/plan-verifier`; do not bypass the gate. |
| Copilot spike fails on Windows | This is the documented upstream timestamp protocol issue; use the deterministic harness fallback. |
| `/readiness` fails | Inspect host logs and verifier path; keep execution fail-closed. |
| Rehearsal mismatch | Treat as a failed demo gate. Do not manually edit evidence to appear successful. |

## Limits and cost assumptions

Local execution uses CPU, memory, disk, and developer time only; it creates no
Azure resources and makes no model calls during the rehearsal. Any dollar cost
model is an **estimate**, not a current Azure price:

`estimated run cost = hosted compute duration + model input/output tokens +
telemetry ingestion/retention + backing stores + network egress`.

Before an Azure trial, obtain current contracted prices from the official
calculator/portal, record region, SKU, token volumes, run frequency, retention,
currency, discounts, and a contingency factor. Do not extrapolate local timing
as hosted capacity or reliability.

Limitations: static console data is not live-bound to the BFF; stores are
in-memory; local headers are not Entra authentication; simulator safety does
not prove production API safety; egress/RBAC/durable audit and hosted
child-process isolation are not exercised; model prose can still mislead;
formal assurance is bounded by `docs/VERIFICATION_SPEC.md`.

## Local teardown

Stop only the recorded BFF/console/host PIDs. Then:

```powershell
Remove-Item -Recurse -Force .\.artifacts\rehearsal -ErrorAction SilentlyContinue
dotnet clean GovernedAgentDemo.sln
Remove-Item -Recurse -Force .\src\GovernedAgent.Console\dist -ErrorAction SilentlyContinue
```

Keep `node_modules` for the next rehearsal, or remove it only when a full
dependency cleanup is intended. Azure teardown is deferred because this local
package provisions nothing; a future deployed environment must use its
reviewed resource inventory and deployment-specific teardown procedure.
