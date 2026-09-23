# Customer Demo Script: Governed Incident-Response Agent

## Demo objective

Demonstrate that an AI agent can investigate and propose operational actions
without being trusted to authorize side effects. Deterministic plan
verification, policy, exact human approval, and the governed tool gateway
remain authoritative.

**Recommended duration:** 10 minutes

## Customer-safe claims

- The repository contains executable local evidence for deterministic
  governance controls.
- Tool use is restricted to an explicit trusted registry.
- Every side effect passes through a governed gateway that independently
  revalidates the action.
- Production writes require an exact, digest-bound, single-use approval.
- Application containment blocks new governed side effects; restoration requires
  re-attestation and incident-commander sign-off.
- Dafny proves selected properties of the bounded deterministic plan model.
- The local audit chain detects mutation.

Do not claim that the whole agent or model is formally verified, that prompt
injection is eliminated, or that the local demo represents production Azure
identity, networking, storage, or operational integrations.

## Before the meeting

From the repository root in PowerShell 7:

```powershell
npm install
dotnet tool restore
pwsh .\scripts\rehearse-local-demo.ps1
```

The rehearsal must finish with `PRESENTER CHECKLIST: PASS`. It writes evidence
to `.artifacts\rehearsal\api-evidence.json`.

Start the visual console:

```powershell
npm run dev --workspace governedagent-console -- --host 127.0.0.1
```

Open `http://127.0.0.1:5173`.

The visual console is a deterministic static presentation. The rehearsal and
BFF API provide the live enforcement evidence.

## 1. Introduce the scenario (1 minute)

**Show:** Incident `INC-1042`, the degraded payments service, and the decision
timeline.

**Say:**

> We have an incident-response agent investigating a degraded payments
> service. The model can analyze evidence and propose actions, but it has no
> authority to execute them. Operational effects are controlled by
> deterministic components outside the model.

Point out the progression from investigation to verified plan, exact approval,
and completed remediation.

## 2. Show the untrusted-input boundary (1 minute)

**Show:** The hostile simulator log under **Untrusted simulator log data**.

**Say:**

> Logs, documents, prompts, model output, and tool responses are all treated as
> untrusted input. This log contains an instruction to disclose credentials,
> but the instruction does not become policy or authority. It remains evidence
> for diagnosis.

> We do not claim that prompt injection is eliminated. Instead, prohibited
> side effects are independently blocked even if model reasoning is influenced.

Explain that the runtime does not expose a general shell, filesystem,
unrestricted URL, or unapproved MCP capability.

## 3. Explain bounded verification and Dafny (2 minutes)

**Show:** The **Verification** panel.

**Say:**

> The agent must express a proposed operation as a structured, bounded plan.
> The executable TypeScript verifier evaluates the concrete plan. Dafny proves
> selected invariants of the corresponding deterministic authorization model.
> The verifier evaluates that structured plan, not the raw prompt: indirect
> prompt injection can influence later model choices, while the proposed action
> is what must cross the deterministic authorization boundary.

The modeled invariants include:

- A tool and its capability must match trusted registry metadata.
- Dependencies may refer only to earlier steps, preventing dependency cycles.
- Production deletes are prohibited.
- Production writes must declare bounded compensation.
- Modeled information cannot flow to a less trusted classification.
- Expired plans cannot become executable.
- Verification is bound to the exact plan digest and approved verifier version.
- Approval is bound to the exact plan, step, action digest, role, and validity
  window.
- Consumed approvals and completed idempotency keys cannot initiate another
  execution.
- Active containment prevents new execution transitions.

**Say:**

> This is formal verification of selected business and authorization rules for
> a bounded deterministic plan model. It is not formal verification of the
> language model, its diagnosis, external tools, or the complete deployed
> system.

## 4. Explain governed tool use (2 minutes)

**Show:** **Containment & recovery** and the current capabilities.

Draw or narrate this path:

```text
Model proposes a tool call
        |
        v
Structured plan validation
        |
        v
Trusted tool-registry binding
        |
        v
Plan verification
        |
        v
Runtime policy evaluation
        |
        v
Exact approval when required
        |
        v
Governed gateway revalidation
        |
        v
Operational tool execution
```

The trusted demo tool registry contains:

- `get_incident`
- `query_metrics`
- `query_logs`
- `get_service_health`
- `update_incident`
- `restart_service`
- `restore_service_state`

**Say:**

> The model cannot invent a tool, promote a read into a write, or label a
> production restart as harmless. Tool name, capability, effect, approval
> class, and resource binding come from trusted application metadata.

> The governed gateway is the only side-effect boundary. It recomputes the
> canonical action digest and checks the registered tool, resource target,
> verification result, policy, execution budget, exact approval, containment,
> idempotency key, and expected resource version.

> The gateway performs these checks independently. A mistaken or bypassed
> pre-tool decision therefore cannot directly cause an operational side effect.

## 5. Run the enforcement rehearsal (3 minutes)

From a second PowerShell terminal, run:

```powershell
pwsh .\scripts\rehearse-local-demo.ps1
```

Narrate each result:

1. **Diagnostic reads are safe.**

   > The incident is reset to a degraded state. Reading logs and metrics does
   > not change the simulator.

2. **The hostile log remains untrusted.**

   > The injected instruction is retained as evidence and explicitly labelled
   > as untrusted.

3. **The production write suspends.**

   > A restart cannot execute on model authorization. The workflow suspends
   > pending an exact incident-commander approval.

4. **The wrong role is denied.**

   > An operator cannot approve an action requiring the incident-commander
   > role.

5. **The exact approval works once.**

   > The valid approval is bound to this action digest and target. A replay is
   > rejected.

6. **Containment stops new writes.**

   > Application containment is checked at the gateway, so a new side effect
   > is denied.

7. **Recovery is ordered and staged.**

   > A governance operator re-attests the known-good version and digest before
   > the control enters read-only recovery. Write authority returns only after
   > an incident commander records root-cause sign-off.

8. **The audit chain verifies.**

   > The local hash-linked audit record detects mutation and correlates the
   > decision with the verified action, containment, re-attestation, and
   > restoration.

## 6. Show the evidence (30 seconds)

Run:

```powershell
Get-Content .\.artifacts\rehearsal\api-evidence.json
```

Point out:

- `containsUntrustedContent`
- the pending approval and required role
- the accepted approval decision
- `containment.mode` changing to `Contained`
- the re-attested known-good version and read-only recovery state
- `restoredControls.mode` returning to `Operational`
- `integrityValid`

## 7. Close (30 seconds)

**Say:**

> This first version demonstrates a governed autonomy pattern: the model
> investigates and proposes, while deterministic verification, policy, human
> approval, and the gateway authorize every side effect.

> The next production step is to replace the simulator and demo identity
> adapter with approved operational integrations, Entra identity, durable
> audit storage, and Azure network and RBAC controls without weakening this
> gateway boundary.

## Questions and answers

### Is the whole agent formally verified?

No. Dafny proves selected invariants of the bounded deterministic plan model.
Model behavior, business outcome correctness, external tool behavior, identity
infrastructure, and the complete runtime remain outside that proof boundary.

### Can the model bypass approval by changing the tool arguments?

The approval is bound to a canonical digest covering the exact plan step and
target. The gateway recomputes that digest and rejects changed arguments,
targets, expired approvals, wrong roles, and replay.

### What happens if the verifier or policy service is unavailable?

The design fails closed. An unavailable or indeterminate verification, policy,
approval, audit, or trusted-metadata dependency must not produce an executable
action.

### Does local containment terminate an operation already running?

The demonstrated invariant prevents new execution transitions. An external
operation that has already started may complete or time out. Production
deployment should combine this application control with layered identity,
traffic, hosting, network, endpoint, and platform controls. Agent 365 can act
as a central containment surface where the connected agent type supports the
relevant management action; it is not a universal process-termination
guarantee for every backing runtime.

### Is the web console live?

The current web console is a deterministic static story. The BFF, tests, and
rehearsal script provide the live local control evidence.

## Fallback

If the browser, Copilot integration, or Foundry connectivity is unavailable,
run:

```powershell
pwsh .\scripts\rehearse-local-demo.ps1
```

Present the terminal checklist and
`.artifacts\rehearsal\api-evidence.json`. Never bypass or weaken a failed
verification, authentication, policy, or protocol check to make the demo pass.
