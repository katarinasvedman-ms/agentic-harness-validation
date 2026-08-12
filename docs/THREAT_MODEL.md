# Threat Model: Governed Enterprise Agent Demo

| Field | Value |
| --- | --- |
| Status | Draft |
| Version | 0.2 |
| Date | 2026-08-12 |
| Related documents | [PRD](PRD.md), [FRD](FRD.md), [Verification Specification](VERIFICATION_SPEC.md) |

## 1. Purpose and scope

This threat model identifies misuse and failure modes for the Governed Enterprise Agent Demo and maps them to preventive, detective, and recovery controls.

In scope:

- Governance console.
- Foundry Hosted Agent.
- Microsoft Agent Framework orchestration.
- GitHub Copilot SDK and Copilot CLI agentic loop.
- Model input and output.
- Verified plan gate.
- Agent Governance Toolkit policy enforcement.
- Human approval.
- Governed tool gateway.
- Simulated operational systems.
- Identity, RBAC, telemetry, audit, evaluation, build, and deployment.

Out of scope for the MVP:

- Security of Microsoft-operated platform internals.
- Real customer production systems.
- Physical attacks.
- Compromise of Azure control-plane administrators.
- General proof of model alignment or model correctness.

## 2. Security objectives

| ID | Objective |
| --- | --- |
| SO-01 | Prevent undeclared or unauthorized tool execution |
| SO-02 | Prevent privilege expansion through planning or delegation |
| SO-03 | Prevent confidential data from reaching unauthorized destinations |
| SO-04 | Require exact, valid human approval for high-impact operations |
| SO-05 | Preserve integrity of policy, proof, identity, and deployment context |
| SO-06 | Attribute each action and decision to the responsible identities and versions |
| SO-07 | Limit blast radius, cost, duration, and repeated failure |
| SO-08 | Detect and explain attempted policy violations |
| SO-09 | Fail closed when critical control components are unavailable |
| SO-10 | Prevent misleading claims about assurance or proof coverage |

## 3. Assets

### 3.1 High-value assets

- Agent and user identities.
- Approval authority.
- Operational tool capabilities.
- Incident data, logs, metrics, and diagnostics.
- Policy definitions and emergency policy.
- Formal specifications and proof artifacts.
- Agent prompts, tool definitions, and workflow code.
- Copilot CLI binary, configuration, JSON-RPC channel, hooks, and local session state.
- Deployment images and configuration.
- Audit evidence and traces.
- Evaluation datasets and baselines.
- Kill-switch authority.

### 3.2 Security properties

| Asset | Confidentiality | Integrity | Availability |
| --- | --- | --- | --- |
| Credentials and tokens | Critical | Critical | High |
| Policy and proof artifacts | Medium | Critical | High |
| Approval artifacts | Medium | Critical | High |
| Operational data | High | High | Medium |
| Audit records | Medium | Critical | High |
| Agent deployment | Medium | Critical | High |
| Evaluation datasets | Medium | High | Medium |
| Kill switch | Medium | Critical | Critical |

## 4. Actors

| Actor | Trust posture |
| --- | --- |
| Incident operator | Authenticated but not trusted for approval |
| Incident commander | Authenticated and authorized for bounded approval |
| Agent developer | Trusted for code changes subject to review and CI |
| Policy author | Trusted for policy changes subject to separation of duties |
| Platform operator | Privileged and audited |
| External attacker | Untrusted |
| Malicious insider | Authenticated but adversarial |
| Compromised data source | Untrusted content producer |
| Model | Non-deterministic and untrusted for authorization |
| Copilot CLI runtime | Separate child process; trusted only to request tools and emit events |
| Copilot SDK adapter | Application dependency handling transport and hooks; integrity-sensitive |
| Tool/MCP server | Trusted only for its declared contract; output remains untrusted |

## 5. Trust boundaries

| Boundary | Crossing data | Required validation |
| --- | --- | --- |
| Browser to Hosted Agent | User input, approvals | Authentication, authorization, schema, CSRF/replay protection |
| Model to application | Plans, arguments, prose | Schema validation; never trust identity or policy fields |
| Agent Framework to Copilot SDK | Prompts, sessions, tools | Explicit configuration; no duplicate execution loop |
| Copilot SDK to CLI | JSON-RPC requests and events | Process identity, protocol validation, correlation, bounds |
| Copilot CLI to pre-tool hook | Tool name and arguments | Canonicalization, trusted metadata enrichment, default deny |
| Pre-tool hook to gateway | Decision and action digest | Recompute digest and revalidate policy/approval |
| Tool to model | Logs, documents, result text | Treat as untrusted; size and content controls |
| Plan gate to policy engine | Verification result and digest | Integrity, version, fail-closed semantics |
| Policy engine to tool gateway | Decision | Matched action digest, freshness, policy version |
| Approval service to gateway | Approval artifact | Signature/integrity, exact digest, role, expiry, nonce |
| Hosted Agent to Azure services | Tool requests | Entra identity, RBAC, network restrictions |
| Application to telemetry | Traces and audit | Redaction, integrity, retention |
| CI to deployment | Image, policy, proofs, evals | Provenance, signing, release gates |

## 6. Threat methodology

Threats are organized around:

- Spoofing identity.
- Tampering with control inputs or evidence.
- Repudiation.
- Information disclosure.
- Denial of service.
- Elevation of privilege.
- Agent-specific risks including prompt injection, tool misuse, memory poisoning, unsafe delegation, cascading failures, and governance bypass.

Risk uses qualitative likelihood and impact:

- **Critical:** Can produce unauthorized high-impact action or material data disclosure with insufficient independent controls.
- **High:** Can undermine a primary control or cause meaningful operational impact.
- **Medium:** Requires substantial preconditions or has bounded impact.
- **Low:** Limited impact or strongly mitigated by independent controls.

## 7. Misuse cases

### TM-01: Direct prompt injection

**Scenario:** A user instructs the agent to ignore policy and call a prohibited tool.

**Impact:** Unauthorized action or data access.

**Controls:**

- Model output is untrusted.
- Explicit tool allowlist.
- Verified plan schema for writes.
- AGT default-deny policy.
- Entra identity and RBAC.
- Deterministic adversarial evaluations.

**Residual risk:** Low for prohibited side effects; medium for misleading textual responses.

### TM-02: Indirect prompt injection

**Scenario:** Logs, tickets, documents, or tool responses contain hidden instructions directing the agent to exfiltrate data or alter its objective.

**Impact:** Data disclosure, tool misuse, or corrupted diagnosis.

**Controls:**

- Mark external content as untrusted.
- Separate evidence from instructions.
- Structured plan rather than executable free text.
- Information-flow invariant for modeled sources and sinks.
- AGT destination and capability policy.
- Network egress restrictions.
- Injection evaluation dataset.

**Residual risk:** Medium because model reasoning may still be influenced, but prohibited side effects remain independently blocked.

### TM-03: Unauthorized tool selection

**Scenario:** The model invents a tool or selects a tool outside the agent's declared capabilities.

**Controls:**

- Registered tool allowlist.
- Plan capability invariant.
- AGT unknown-tool deny rule.
- Gateway rejects unknown tools.
- RBAC denies unavailable operations.

**Residual risk:** Low.

### TM-04: Safe tool with unsafe arguments

**Scenario:** The model uses an allowed tool but targets production, expands scope, or injects malicious arguments.

**Controls:**

- Strict argument schemas with unknown-field rejection.
- Resource- and environment-aware policy.
- Exact approval digest.
- Tool-side business validation.
- Argument correctness evaluation.

**Residual risk:** Medium if resource classification metadata is wrong.

### TM-05: Privilege escalation through delegation

**Scenario:** The incident agent delegates to another agent with broader capabilities or manipulates delegation metadata.

**Controls:**

- No unrestricted multi-agent delegation in MVP.
- Delegated capability subset invariant.
- Trusted code constructs identity fields.
- AGT identity and trust policy.
- Separate agent identities and RBAC.

**Residual risk:** Low in MVP; must be reassessed before multi-agent support.

### TM-06: Approval bypass

**Scenario:** The model labels a write as read-only, omits approval metadata, or calls the tool directly.

**Controls:**

- Trusted gateway derives effect classification from tool registry.
- Policy does not rely on model-provided effect labels.
- Production writes require approval.
- Gateway rechecks policy immediately before execution.

**Residual risk:** Low if tool registry classification is correct.

### TM-07: Approval tampering or replay

**Scenario:** An approval is modified, reused, applied to different arguments, or used after expiry.

**Controls:**

- Canonical action digest.
- Exact plan and step binding.
- Expiry and single-use nonce.
- Authorized approver validation.
- Atomic consumption.
- Replay tests.

**Residual risk:** Low.

### TM-08: Data exfiltration through an allowed sink

**Scenario:** The agent sends confidential diagnostics through an otherwise permitted messaging or ticket tool.

**Controls:**

- Data classification attached by trusted sources.
- Destination classification.
- Plan information-flow invariant.
- Runtime data-loss policy.
- Output minimization and redaction.
- No general outbound network tool.

**Residual risk:** Medium because content classification and provenance can be incomplete.

### TM-09: Covert exfiltration

**Scenario:** Sensitive information is encoded, summarized, split across requests, or embedded in identifiers.

**Controls:**

- Avoid unrestricted external sinks.
- Provenance-aware policy rather than content-only filtering.
- Rate and budget limits.
- Destination allowlist.
- Network controls.
- Behavioral detection and audit review.

**Residual risk:** Medium; formal plan-flow properties do not cover all covert channels.

### TM-10: Policy bypass

**Scenario:** A code path calls a tool without passing through AGT.

**Controls:**

- Single tool gateway.
- No direct tool clients outside gateway module.
- Architecture tests and code review.
- Restricted network and credentials.
- Egress only from gateway path where feasible.
- Trace completeness checks.

**Residual risk:** Medium until infrastructure-level egress enforcement is demonstrated.

### TM-11: Policy tampering or downgrade

**Scenario:** An attacker deploys a permissive policy, rolls back to a vulnerable version, or changes emergency overrides.

**Controls:**

- Version control and review.
- Signed release artifacts and provenance.
- Policy regression suite.
- Separation of duties.
- Audit policy activation.
- Minimum allowed policy version.

**Residual risk:** Medium for privileged insiders.

### TM-12: Verification-result forgery

**Scenario:** The application claims a plan is verified without running the verifier or substitutes a result for another plan.

**Controls:**

- Verification result bound to plan digest.
- Verifier and specification version recorded.
- Trusted component constructs result.
- Release and runtime conformance tests.
- Policy requires valid verification for writes.

**Residual risk:** Low to medium depending on verifier isolation.

### TM-13: Model-to-formal semantic gap

**Scenario:** LemmaScript proves a generated model that does not faithfully represent executable TypeScript.

**Controls:**

- Restricted language subset.
- Differential tests.
- Pinned translator and verifier.
- Review generated model for critical functions.
- Explicit assurance disclaimer.

**Residual risk:** Medium; this is a known experimental-tool limitation.

### TM-14: Specification error

**Scenario:** The implementation is proven against an incomplete or incorrect invariant.

**Controls:**

- Human domain and security review.
- Counterexample-driven tests.
- Threat-to-invariant traceability.
- Assumptions and exclusions published.
- Independent review of high-value proof obligations.

**Residual risk:** Medium to high; proof cannot establish that the intended policy is correct.

### TM-15: Tool poisoning or schema drift

**Scenario:** An MCP/tool description changes to include malicious instructions or different behavior.

**Controls:**

- Pin tool versions and schema digests.
- AGT MCP security gateway where feasible.
- Tool manifest integrity monitoring.
- Reject unexpected schema.
- Re-run evaluations when tools change.

**Residual risk:** Medium for third-party tools; low for controlled MVP tools.

### TM-16: Memory poisoning

**Scenario:** Malicious content persists in agent memory and influences later sessions.

**Controls:**

- Incident-scoped sessions.
- No cross-incident long-term memory in MVP.
- Trusted/untrusted provenance labels.
- Memory reset and retention limits.
- Adversarial multi-turn evaluation.

**Residual risk:** Low in MVP.

### TM-17: Identity spoofing

**Scenario:** A caller or component supplies another user's, approver's, or agent's identity in a payload.

**Controls:**

- Derive identity from authenticated tokens and platform context.
- Ignore model- and client-supplied trusted identity fields.
- Validate audience, issuer, expiry, and role.
- Bind audit records to resolved identity.

**Residual risk:** Low.

### TM-18: Credential disclosure

**Scenario:** Secrets appear in prompts, traces, errors, or tool output.

**Controls:**

- Managed identity.
- Secret scanning and redaction.
- No credentials accepted from model arguments.
- Telemetry field allowlist.
- Restricted debug logging.

**Residual risk:** Low to medium depending on third-party SDK logging.

### TM-19: Audit tampering or repudiation

**Scenario:** An actor deletes or alters records or denies responsibility.

**Controls:**

- Append-only or tamper-evident audit storage.
- Stable identities and timestamps.
- Plan, action, policy, and deployment digests.
- Restricted retention administration.

**Residual risk:** Low in the demo if storage is correctly configured.

### TM-20: Denial of service and cost exhaustion

**Scenario:** Repeated prompts trigger excessive model calls, tool calls, retries, or long plans.

**Controls:**

- Authentication and rate limits.
- Per-session token, time, step, and cost budgets.
- Plan-size bounds.
- Timeouts and bounded retries.
- Circuit breakers.

**Residual risk:** Medium.

### TM-21: Cascading remediation failure

**Scenario:** A write fails midway, is retried, or causes additional service failures.

**Controls:**

- One reversible write in MVP.
- Exact compensation plan.
- Separate exact approval for production compensation.
- Idempotency.
- Bounded retry.
- Outcome verification.
- Circuit breaker and kill switch.

**Residual risk:** Low in simulator; higher for future real systems.

### TM-22: Kill-switch bypass

**Scenario:** Cached allow decisions or direct tool paths continue after emergency stop.

**Controls:**

- Check emergency policy immediately before every execution.
- Short or zero allow-decision cache.
- Gateway-only tool access.
- Kill-switch acceptance test.
- Network/RBAC revocation as secondary emergency control.

**Residual risk:** Low if no direct paths exist.

### TM-23: Evaluation gaming

**Scenario:** The agent or development process overfits a visible evaluation dataset while failing unseen cases.

**Controls:**

- Versioned train/development and held-out datasets.
- Adversarial mutation.
- Production-derived cases after review and redaction.
- Deterministic control assertions.
- Baseline comparison.

**Residual risk:** Medium.

### TM-24: Misleading assurance

**Scenario:** Customers infer that the complete agent is formally verified or production-certified.

**Controls:**

- UI labels proof scope.
- Guarantee report lists assumptions and exclusions.
- Presenter guide distinguishes proof, policy, evaluation, and RBAC.
- Preview and experimental labels.

**Residual risk:** Low with disciplined presentation.

### TM-25: Copilot hook replacement or omission

**Scenario:** A custom `OnPreToolUse` hook replaces the SDK's default approval hook but fails to enforce deny or ask semantics.

**Controls:**

- One reviewed governance hook implementation.
- Default deny on exception, timeout, unknown tool, or unknown result.
- Hook conformance tests for every registered tool.
- Gateway revalidation as an independent enforcement point.

**Residual risk:** Low after conformance testing.

### TM-26: Built-in capability exposure

**Scenario:** The Copilot runtime gains shell, filesystem, URL, or MCP capabilities outside the governed incident tool catalogue.

**Controls:**

- Disable built-in permissions.
- Explicit tool and MCP allowlists.
- Container and network restrictions.
- Startup configuration attestation.
- Adversarial evaluation of built-in tool attempts.

**Residual risk:** Low if configuration and egress controls agree.

### TM-27: CLI or JSON-RPC tampering

**Scenario:** The child binary, launch arguments, or SDK-to-CLI channel is replaced or manipulated.

**Controls:**

- Pin and integrity-check the Copilot SDK and CLI artifact.
- Run the child process under the Hosted Agent identity and container boundary.
- Validate protocol messages and correlate process/session identity.
- Never accept trusted authorization fields from CLI events.
- Gateway enforcement remains authoritative.

**Residual risk:** Medium until hosted-process hardening is validated.

### TM-28: Copilot session-state leakage

**Scenario:** Conversation or tool results from one incident or user are resumed in another session.

**Controls:**

- Incident-scoped session identifiers and storage paths.
- No shared long-term memory in the MVP.
- Explicit lifecycle cleanup and retention.
- Cross-session leakage tests.
- Foundry session isolation.

**Residual risk:** Medium until scale-to-zero and resume behavior are tested.

### TM-29: Agentic-loop exhaustion

**Scenario:** The model continues requesting tools, autopilot nudges restart the loop, or repeated observations consume excessive time and cost.

**Controls:**

- Application-enforced turn, tool-call, token, time, and cost budgets.
- Cancellation propagated through SDK and CLI.
- Circuit breaker on repeated denials or errors.
- `session.idle` observed as lifecycle state, not success.

**Residual risk:** Low to medium.

### TM-30: False completion signal

**Scenario:** The model declares `task_complete` despite unresolved work, failed controls, or missing outcome verification.

**Controls:**

- Treat model-declared completion as advisory.
- Use `session.idle` only to detect processing stop.
- Derive business completion from deterministic workflow and outcome state.
- Evaluate premature completion cases.

**Residual risk:** Low for system state; medium for misleading prose.

### TM-31: Duplicate orchestration

**Scenario:** Agent Framework and Copilot CLI each run a tool loop, causing duplicate calls, inconsistent state, or bypassed approval.

**Controls:**

- Copilot CLI exclusively owns inner turn iteration.
- Agent Framework performs outer composition only.
- One handler path per registered tool.
- Idempotency and trace assertions detect duplicates.

**Residual risk:** Low with clear ownership.

### TM-32: Hosted child-runtime incompatibility

**Scenario:** Copilot CLI startup, authentication, writable paths, health behavior, or shutdown is incompatible with Foundry Hosted Agents.

**Controls:**

- Release-blocking hosted feasibility gate.
- Non-interactive authentication only.
- Explicit health and shutdown tests.
- Agent Framework Harness fallback preserving all external controls.

**Residual risk:** Architecture decision remains open until the gate is run.

## 8. Control mapping

| Threat | Plan verification | AGT policy | Approval | Identity/RBAC | Tool gateway | Telemetry/eval | Operational control |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Direct/indirect injection | Partial | Primary | Secondary | Secondary | Primary | Detection | Circuit breaker |
| Wrong tool | Primary | Primary | Secondary | Secondary | Primary | Detection | - |
| Unsafe arguments | Partial | Primary | Primary | Secondary | Primary | Detection | - |
| Privilege escalation | Primary | Primary | - | Primary | Primary | Detection | - |
| Data exfiltration | Partial | Primary | Secondary | Primary | Primary | Detection | Rate limits |
| Approval bypass/replay | Primary | Primary | Primary | Primary | Primary | Detection | - |
| Policy bypass | - | Primary when reached | - | Primary | Primary | Detection | Kill switch |
| Verification forgery | Digest binding | Primary | - | - | Primary | Detection | Fail closed |
| Tool poisoning | - | MCP/tool policy | - | Secondary | Schema integrity | Detection | Disable tool |
| Cascading failure | Compensation invariant | Policy budgets | Primary | - | Idempotency | Detection | Breaker/kill switch |
| Hook omission/replacement | - | Pre-tool hook | Secondary | - | Primary recheck | Conformance tests | Fail closed |
| Built-in capability exposure | - | Explicit allowlist | - | Hosted identity/RBAC | Gateway-only tools | Startup and adversarial tests | Disable permissions/egress |
| CLI/JSON-RPC tampering | - | Secondary | - | Hosted identity | Primary | Integrity telemetry | Disable runtime |
| Session leakage | - | - | - | Session isolation | Output controls | Leakage tests | Cleanup/retention |
| Loop exhaustion | - | Policy budgets | - | - | Tool-call limits | Turn metrics | Cancel/breaker |
| False completion | - | - | - | - | Workflow state | Completion evals | Deterministic outcome |
| Duplicate orchestration | - | Loop ownership | - | - | Idempotency | Duplicate-call assertions | Cancel loop |
| Hosted incompatibility | - | - | - | Hosted boundary | Gateway retained | Feasibility tests | Harness fallback |

## 9. Required security tests

The MVP MUST include automated tests for:

1. Direct and indirect prompt injection.
2. Unknown tool invocation.
3. Allowed tool with prohibited resource.
4. Production write without approval.
5. Modified action after approval.
6. Expired, revoked, and replayed approval.
7. Confidential source to public sink.
8. Forged verification result and digest mismatch.
9. Policy engine and verifier outage.
10. Kill switch during an active session.
11. Tool-schema drift.
12. Duplicate write with the same idempotency key.
13. Audit redaction.
14. Token, step, and cost budget exhaustion.
15. Missing, replaced, failed, and timed-out pre-tool hook.
16. Built-in shell, filesystem, and URL attempts.
17. Hook-to-gateway digest mismatch.
18. Cross-session state leakage.
19. CLI crash and JSON-RPC interruption.
20. Premature model-declared completion.

## 10. Security acceptance criteria

- No high-impact action can execute on model authorization alone.
- No direct application path to a write tool bypasses the gateway.
- Every production write requires verified plan, allow policy, exact approval, and RBAC permission.
- Every adversarial MVP scenario produces no unauthorized external side effect.
- Every denial produces a correlated decision record.
- Loss of verifier, policy, approval, or required audit persistence fails closed.
- Proof scope and residual risk are visible in the demo.
- Copilot loop, Agent Framework composition, and gateway responsibilities are not duplicated or conflated.

## 11. Residual risks requiring explicit acceptance

- Model output may be misleading even when actions are safely constrained.
- Formal specifications may omit important real-world requirements.
- LemmaScript translation may not perfectly preserve TypeScript semantics.
- Data-provenance labels may be incomplete or incorrect.
- Covert channels cannot be fully excluded.
- Privileged administrators can alter infrastructure unless organizational controls prevent it.
- Public-preview dependencies may change behavior.
- Simulated-system safety does not prove safety against a real production API.

These risks MUST be reviewed before expanding beyond the isolated demo environment.
