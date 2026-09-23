# ADR 0002: Authorize Verified Intent with Task-Scoped Capability Leases

| Field | Value |
| --- | --- |
| Status | Accepted for the local reference implementation |
| Date | 2026-09-23 |
| Decision owners | Product and architecture team |

## Context

The model sees user prompts, retrieved evidence, and tool output, all of which
can be incomplete or adversarial. A literal prompt therefore cannot reliably
describe the action the agent will later attempt. The governed boundary needs
an authorization input derived from the structured plan and trusted
application metadata.

Exact human approval already authorizes one immutable production action, but it
is not itself an execution capability. The local demo needs to show the
additional step between approval and side effect: minting narrowly scoped,
short-lived authority for the verified action.

## Decision

The structured plan is authoritative for action intent. The tool registry
assigns a closed `IntentClass`; the model cannot supply or override it. Intent,
capability, effect, resource, environment, arguments, policy version, verifier
version, and identity context are included in the canonical authorization
binding.

After plan verification, policy allow, containment admission, and any required
exact approval, the governed gateway issues an application-layer capability
lease. The lease:

- Is bound to the user, agent, deployment, session, incident, plan, step, plan
  digest, action digest, intent, tool, capability, effect, target, environment,
  policy version, and verifier version.
- Has a maximum lifetime of five minutes; the demonstrated write lease lasts
  90 seconds.
- Permits exactly one use.
- Must be atomically consumed before the tool handler runs.
- Is completed after successful execution or revoked when execution cannot
  complete.
- Is revoked when matching agent or session containment is activated.
- Is represented in the hash-linked audit record without exposing its secret
  nonce through the console API.

The local lease is not an Entra token, Azure RBAC assignment, APIM credential,
or downstream service credential. The agent receives no operational
credential. In production, Entra identity, RBAC, network policy, and service
authorization remain the privilege ceiling; the application lease adds exact
task-level authorization inside that boundary.

## Rationale

- Grounds authorization in the action the agent proposes, not prompt wording.
- Makes ephemeral, task-scoped privilege visible and testable without Azure
  resources or credentials.
- Prevents lease replay and confused-deputy use through exact binding.
- Keeps the governed gateway as the sole side-effect boundary.
- Allows containment to invalidate outstanding task authority.

## Consequences

Positive:

- The demo now shows approve, lease, consume, execute, and close as separate
  auditable events.
- Prompt semantic analysis can remain advisory without becoming an
  authorization oracle.
- The same contract can later front production token exchange or workload
  identity mechanisms.

Negative:

- The in-memory broker and audit are process-local and non-durable.
- Lease issuance currently occurs inside the gateway immediately before
  execution rather than through a distributed privilege broker.
- Revocation cannot undo a side effect that already passed atomic execution
  admission.
- The Dafny plan proof does not prove distributed lease, identity, or
  revocation behavior; those properties currently rely on executable tests.

## Alternatives considered

### Authorize from the raw prompt

Rejected because indirect prompt injection and later planning can make the
executed action diverge from the literal user text.

### Treat approval as the execution credential

Rejected because approval captures human authorization but should not be
passed to downstream systems or reused as ambient authority.

### Mint an Entra token or dynamic RBAC assignment in the local demo

Deferred. It would require Azure resources and credentials, weaken the
credential-free rehearsal, and conflate application authorization with the
production identity privilege ceiling.

