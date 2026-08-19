# Local demo guide

This rehearsal is deterministic and credential-free. It demonstrates the local
simulator and governance controls; it does **not** demonstrate a live Copilot
session, Foundry authentication, Azure deployment, or a production system.

## Opening scene

Start with the incident story before showing the governance model:

> Payments API goes down. An agent investigates, proposes a fix, and executes it
> with a human approving the one step that actually touches production. Now
> imagine the same agent, same incident, except this time there is a prompt
> injection hidden in the diagnostic logs trying to get it to exfiltrate data to
> an external endpoint. Same agent. Same autonomy. Completely different outcome,
> because the system, not the model, decided what was allowed.

Then make the scale transition explicit:

> That is one governed agent. The harder problem is doing this consistently
> across a portfolio you did not build and cannot fully see yet. The rest of the
> story is how to govern, inventory, observe, and reuse agents at enterprise
> scale.

## Setup and preflight

From the repository root in PowerShell 7:

```powershell
npm install
dotnet tool restore
pwsh .\scripts\rehearse-local-demo.ps1
```

The rehearsal exits nonzero on any mismatch and writes API evidence under
`.artifacts\rehearsal`. For the visual console, use two terminals:

```powershell
# terminal 1: static customer-facing console
npm run dev --workspace governedagent-console -- --host 127.0.0.1

# terminal 2: live local BFF API
dotnet run --project .\src\GovernedAgent.Console.Bff -- --urls http://127.0.0.1:5072
```

Open `http://127.0.0.1:5173`. The UI is intentionally a deterministic static
story labelled **Local mock · no backend**; use the API commands below for live
control evidence.

## 3–5 minute executive path

1. **Open the incident (45 seconds).** Show `INC-1042`, the degraded payments
   instance, and the decision timeline. Say: “The model can investigate and
   propose, but it cannot authorize a side effect.”
2. **Show hostile evidence (45 seconds).** Point to **Untrusted simulator log
   data**. Say: “The injection is retained as evidence, not promoted to an
   instruction. Deterministic controls remain authoritative even if model text
   is influenced.”
3. **Show the bounded plan (60 seconds).** Point to Verification and Exact
   approval. Say: “This demo verifies a bounded plan schema and binds one
   production write to its action digest, target, expiry, and approver role.
   This is not a proof of the model or a production certification.”
4. **Show independent enforcement (45 seconds).** Point to Policy & kill
   switch. Say: “The governed gateway is the only side-effect boundary. It
   rechecks registry metadata, policy, budget, exact approval, idempotency, and
   emergency stop.”
5. **Close on evidence (30 seconds).** Point to Audit chain. Say: “Every local
   decision is correlated and the in-memory hash chain is verified. Durable
   Azure audit storage remains deployment work.”

Expected UI evidence: injection marked as data, plan passed, exact single-use
approval, remediation complete, gateway active, and audit chain verified.

## 12–15 minute technical path

Run `pwsh .\scripts\rehearse-local-demo.ps1` and narrate its checklist:

1. **Reset and diagnosis (2 minutes).** The BFF resets `INC-1042`; `/health`
   reports healthy; incident evidence shows a degraded `payments-api-03`,
   diagnostic metrics, and `containsUntrustedContent=true`.
2. **Trust boundary (2 minutes).** Show the injected log string in
   `.artifacts\rehearsal\api-evidence.json`. Say: “The content remains model
   input. The diagnostic read is side-effect free; separate gateway tests prove
   that mutated digests and unknown arguments cannot cross the trusted action
   boundary.” This is layered evidence, not a claim that the current
   deterministic workflow executes an end-to-end model prompt-injection trial.
3. **Verification and suspension (3 minutes).** The represented plan is checked
   by the shipped Node verifier in `ConsoleBffTests`. The real local workflow
   test asserts the
   production restart suspends with the simulator still degraded.
4. **Exact approval (3 minutes).** The API rejects a wrong role, accepts the
   exact incident-commander decision once, and rejects replay. Workflow tests
   separately prove wrong digest denial, valid resume, one restart, and healthy
   completion.
5. **Emergency control (2 minutes).** The API activates the kill switch and
   reads it back. Gateway tests prove a new write is denied and no side effect
   occurs.
6. **Audit and limits (2 minutes).** The API returns `integrityValid=true`;
   explain that the local chain, identities, policy, and approval stores are
   in-memory. Open
   [the architecture source](architecture/governed-agent-runtime.excalidraw)
   in `https://aka.ms/excalidraw` if a deeper boundary discussion is useful.

Useful live reads:

```powershell
irm http://127.0.0.1:5072/health
irm http://127.0.0.1:5072/api/incidents/INC-1042
irm http://127.0.0.1:5072/api/incidents/INC-1042/evidence
irm http://127.0.0.1:5072/api/incidents/INC-1042/plan-verification
irm http://127.0.0.1:5072/api/audit
```

## Customer-safe claims and fallback

Safe claims: the repository contains deterministic local enforcement and tests;
the simulator write is gateway-controlled; exact approvals are digest-bound and
single-use; the bounded verifier and published assumptions have executable
evidence; the local audit chain detects mutation.

Do not claim that the whole agent is formally verified, that prompt injection
is eliminated, that Azure RBAC/egress/durable audit is configured, or that
remote Copilot/Foundry authentication and deployment work.

If Copilot or Foundry is unavailable, stay on the deterministic console, run
the rehearsal, and show its JSON/test evidence. Describe Copilot as the
untrusted inner loop and Foundry as the planned host boundary, not as live
evidence. Never weaken a failed authentication or protocol check for the demo.
