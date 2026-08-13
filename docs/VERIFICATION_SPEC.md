# Verification Specification: Governed Enterprise Agent Demo

| Field | Value |
| --- | --- |
| Status | Draft |
| Version | 0.2 |
| Date | 2026-08-12 |
| Related documents | [PRD](PRD.md), [FRD](FRD.md), [Threat Model](THREAT_MODEL.md) |

## 1. Purpose

This specification defines the bounded formal-verification work for the Governed Enterprise Agent Demo.

It intentionally does not attempt to prove the language model, GitHub Copilot SDK or CLI loop, the complete Hosted Agent, Microsoft Foundry, Microsoft Agent Framework, the Agent Governance Toolkit, Azure, or the distributed system correct.

The verification target is a small deterministic authorization model that decides whether a structured agent plan is eligible to proceed to runtime policy evaluation and approval.

## 2. Assurance claim

Subject to the assumptions in this document:

> For every plan accepted by the verified plan gate, the modeled structural safety invariants hold for all plan states and inputs represented by the formal model.

This claim does not mean:

- The plan will achieve the user's business objective.
- The model's diagnosis is factually correct.
- External tools behave according to their contracts.
- Runtime identity, policy, approval, or infrastructure configuration is correct.
- Unmodeled data flows or covert channels are safe.
- The TypeScript-to-Dafny translation is free from semantic defects.
- Copilot's loop termination or model-declared completion is correct.

## 3. Verification strategy

### 3.1 Initial approach

The MVP uses:

- A restricted, deterministic TypeScript implementation of the plan validator.
- LemmaScript annotations for preconditions, postconditions, and invariants.
- Dafny as the primary proof backend.
- Differential tests comparing executable TypeScript results with the generated/formal model.
- A human-readable guarantee report.
- CI enforcement of required proof obligations.

lemmafit MAY be used for a separate bounded governance-console state machine. It is not a runtime dependency of the authorization boundary.

### 3.2 Future approach

A future version MAY:

- Implement the validator in Rust.
- Use Verus for Rust-native verification.
- Retain Dafny as an independent reference model.
- Validate proof-carrying plans using a small isolated checker.

## 4. Formal model

### 4.1 Primitive types

The model defines finite or bounded representations of:

- `AgentId`
- `IncidentId`
- `PlanId`
- `StepId`
- `ToolId`
- `Capability`
- `ResourceId`
- `Environment`
- `DataClassification`
- `DestinationClassification`
- `Effect`
- `ApprovalClass`
- `Timestamp`
- `Digest`

### 4.2 Enumerations

```text
Environment = Development | Test | Production
Effect = Read | Write | Delete
DataClassification = Public | Internal | InternalTrusted | Confidential | Restricted
Decision = Verified | Rejected | Indeterminate
ExecutionState =
    Proposed
  | Verified
  | AwaitingApproval
  | Approved
  | Executing
  | Completed
  | Denied
  | Failed
  | Compensating
  | Compensated
```

### 4.3 Capability set

An agent has an immutable set of declared capabilities for one plan evaluation:

```text
Capabilities(agent) = finite set of Capability
```

The model does not infer capabilities from prompts or tool descriptions.

### 4.4 Plan

A plan contains:

- Plan identity, agent identity, incident identity, schema version, and expiry.
- A finite ordered sequence of steps.
- For each step: tool, capability, resource, effect, dependencies, data sources, destination, required approval class, and optional compensation.

### 4.5 Approval

An approval is modeled as:

```text
Approval {
  approverId,
  approverRoles,
  planId,
  stepId,
  actionDigest,
  issuedAt,
  expiresAt,
  nonce,
  consumed,
  revoked
}
```

Cryptographic signature correctness is outside the proof model and is a runtime assumption.

### 4.6 Runtime state

The bounded console/workflow model contains:

```text
RuntimeState {
  killSwitchActive,
  stepStates,
  consumedApprovalNonces,
  executedIdempotencyKeys
}
```

## 5. Preconditions

The verifier assumes or checks:

- `PRE-01`: The plan uses the supported schema version.
- `PRE-02`: Plan and step identifiers are non-empty and canonical.
- `PRE-03`: The plan has at least one step and no more than the configured maximum.
- `PRE-04`: Every capability, tool, effect, classification, and environment maps to a known enumeration value.
- `PRE-05`: Trusted agent capability declarations originate outside model output.
- `PRE-06`: Trusted tool metadata originates from the pinned tool registry.
- `PRE-07`: The current time supplied to validation is monotonic enough for plan and approval expiry checks.
- `PRE-08`: Canonical serialization produces the same digest for semantically identical supported values.
- `PRE-09`: Copilot tool requests are parsed as untrusted inputs before trusted registry and identity metadata are added.

Failure to establish a required precondition yields `Rejected` or `Indeterminate`, never `Verified`.

## 6. Required invariants

### INV-01: Declared capability confinement

For every step in an accepted plan:

```text
step.capability is in Capabilities(plan.agentId)
```

No accepted plan may introduce a capability not declared for the agent.

### INV-02: Registered tool-capability binding

For every step:

```text
ToolRegistry[step.tool].capability == step.capability
AND ToolRegistry[step.tool].effect == step.effect
```

The model MUST use trusted registry metadata rather than model-provided labels.

### INV-03: Dependency integrity

For every dependency of every step:

```text
dependency references exactly one step in the same plan
AND dependency occurs before the dependent step
AND step does not directly or transitively depend on itself
```

Therefore, the accepted plan dependency graph is acyclic.

### INV-04: Production write approval

For any transition from `AwaitingApproval` or `Approved` to `Executing` where:

```text
environment == Production
AND effect in {Write, Delete}
```

there MUST exist a valid, unexpired, unrevoked, unconsumed approval whose role and action digest match the exact step.

### INV-05: Production delete prohibition

For every accepted MVP plan:

```text
NOT (step.environment == Production AND step.effect == Delete)
```

Production deletion is prohibited, not approval-eligible.

### INV-06: Approval non-transferability

An approval valid for one action cannot authorize any action with a different:

- Plan ID.
- Step ID.
- Tool.
- Resource.
- Environment.
- Canonical arguments.
- Policy-relevant metadata.

This is represented by equality of the canonical action digest and bound identifiers.

### INV-07: Approval single use

For every approval nonce:

```text
count(executions authorized by nonce) <= 1
```

Once consumed, the nonce cannot authorize another transition to `Executing`.

### INV-08: Denied-state terminality

For every step:

```text
state == Denied
implies nextState not in {Approved, Executing, Completed}
```

A new plan and step identity are required to retry a denied operation.

### INV-09: Completed-action non-reexecution

For every idempotency key:

```text
count(external write executions) <= 1
```

The formal state machine proves that the workflow does not intentionally issue a second execution transition for a completed key. Atomic enforcement by the real tool is a runtime assumption.

### INV-10: Kill-switch safety

For all transitions:

```text
killSwitchActive
implies nextState != Executing
```

The MVP proof covers new execution transitions. Behavior of an already executing external operation is outside this invariant.

### INV-11: Modeled information-flow confinement

For every accepted plan step that transfers modeled data:

```text
classification(source) <= permittedClassification(destination)
```

The MVP uses one ordered five-level lattice:

```text
Public < Internal < InternalTrusted < Confidential < Restricted
```

Sources may flow only to destinations at the same or a higher lattice level. A public-untrusted destination is represented by `Public`.

This property follows explicit provenance and destination labels. It does not inspect arbitrary natural-language content.

### INV-12: Delegation non-escalation

If delegation is enabled:

```text
Capabilities(childAgent) is a subset of
Capabilities(parentAgent) intersect DelegatedCapabilities
```

For the MVP, delegation is disabled; the invariant is satisfied by construction. Enabling delegation requires executable proof cases.

### INV-13: Compensation declaration

For every accepted production write:

```text
step.compensation exists
AND compensation.tool is registered
AND compensation.capability is declared
AND compensation targets the same bounded resource scope
```

This proves declaration and scope, not that compensation will restore the real system.

Compensation is a distinct write action. In the MVP it requires a separate exact approval after failure; approval of the original action cannot authorize compensation.

### INV-14: Plan expiry

An expired plan cannot transition to `Verified`, `Approved`, or `Executing`.

### INV-15: Schema closure

Security-relevant objects with unknown fields or unknown enumeration values cannot be accepted.

### INV-16: Verification-result binding

A verification result is applicable only when:

```text
result.planDigest == digest(canonicalPlan)
AND result.specificationVersion is allowed
AND result.verifierVersion is allowed
```

Runtime integrity of the result is an implementation obligation.

## 7. State transition specification

### 7.1 Permitted transitions

| Current state | Next state | Guard |
| --- | --- | --- |
| Proposed | Verified | All required plan invariants hold |
| Proposed | Denied | Any invariant is false |
| Proposed | Failed | Validation cannot complete |
| Verified | AwaitingApproval | Runtime policy requires approval |
| Verified | Executing | Runtime policy allows and approval is not required |
| Verified | Denied | Runtime policy denies |
| AwaitingApproval | Approved | Exact valid approval exists |
| AwaitingApproval | Denied | Rejected, revoked, expired, or kill switch |
| Approved | Executing | Policy and approval revalidated; kill switch inactive |
| Executing | Completed | Tool reports success and outcome check passes |
| Executing | Failed | Tool or outcome check fails |
| Failed | Compensating | Compensation has a separately verified action, runtime allow decision, and exact valid approval |
| Compensating | Compensated | Compensation reports success |

All unlisted transitions are invalid.

### 7.2 Global transition guards

No transition to `Executing` is allowed when:

- Kill switch is active.
- Plan is expired.
- Verification binding is invalid.
- Runtime policy is not allow.
- Required approval is absent or invalid.
- Idempotency key is already executed.

If the kill switch activates after a tool attempt has already started, the current attempt may complete or reach its timeout, but the workflow cannot initiate a retry, compensation, or any other new execution transition.

## 8. Proof obligations

| ID | Obligation | Method |
| --- | --- | --- |
| PO-01 | Validator acceptance implies INV-01 through INV-05 | Dafny postconditions and lemmas |
| PO-02 | Dependency validation implies acyclic ordered graph | Sequence/index proof |
| PO-03 | Approval validation implies exact digest and freshness | Dafny predicate and postcondition |
| PO-04 | State transitions preserve INV-07 through INV-10 | Inductive state invariant |
| PO-05 | Accepted modeled flows satisfy INV-11 | Classification lattice proof |
| PO-06 | Delegation preserves capability subset | Set inclusion proof; construction-only in MVP |
| PO-07 | Accepted production writes declare bounded compensation | Validator postcondition |
| PO-08 | Expired plans cannot become executable | Transition proof |
| PO-09 | Unknown schema elements cannot be accepted | Parser/validator closure plus tests |
| PO-10 | Verification applies only to matching digest and versions | Binding predicate proof |

## 9. Executable implementation constraints

The verified TypeScript module MUST:

- Be deterministic.
- Have no network, filesystem, environment-variable, clock, random, or process access.
- Receive time and trusted registries as explicit immutable inputs.
- Avoid reflection, dynamic code evaluation, proxies, and prototype mutation.
- Use a documented LemmaScript-supported subset.
- Reject unsupported constructs during verification.
- Keep parsing separate from verified semantic validation.
- Return structured errors without broad exception suppression.

## 10. Assumptions

### A-01: Trusted input provenance

Agent capabilities, tool registry metadata, policy-required approval classes, current time, and identity fields are supplied by trusted application or platform code.

### A-02: Canonicalization

The runtime canonicalization and digest implementation are deterministic and collision-resistant for the supported domain.

### A-03: Translation fidelity

LemmaScript correctly translates the supported TypeScript subset into the Dafny model. Differential testing reduces but does not eliminate this assumption.

### A-04: Verifier correctness

Dafny and its solver correctly validate the generated proof obligations for the pinned versions.

### A-05: Runtime calls the gate

Every write-capable execution path invokes the verified plan gate and checks its result.

### A-06: Runtime policy enforcement

AGT and the tool gateway correctly enforce runtime policy after plan verification.

### A-07: Identity and approval integrity

Entra authentication, approver role resolution, approval integrity, and nonce consumption behave according to their runtime contracts.

### A-08: Tool contract

Registered tools enforce their argument schema, identity, idempotency, and resource boundaries.

### A-09: Data labels

Source and destination classifications supplied to the model are correct. The proof does not derive labels from arbitrary content.

### A-10: Bounded plan

The accepted plan size, identifiers, and enumerations remain within documented bounds.

### A-11: Copilot hook coverage

Every registered Copilot custom tool request invokes the configured pre-tool governance hook before its handler.

### A-12: Hook and gateway canonicalization

The pre-tool hook and governed gateway implement the same canonical action schema and digest rules.

### A-13: Gateway-only execution

Every operational tool handler delegates to the governed gateway, which independently revalidates policy, approval, verification binding, budgets, kill switch, and idempotency.

### A-14: Loop lifecycle interpretation

The application treats `session.idle` only as a processing lifecycle event and does not infer successful business completion from it or from model-declared task completion.

## 11. Explicit exclusions

The formal proof does not cover:

- Correctness, safety, alignment, or honesty of the LLM.
- Natural-language interpretation.
- Diagnostic factual accuracy.
- Completeness or correctness of the human-authored specification.
- Correctness of Foundry, Agent Framework, AGT, Azure RBAC, Entra ID, browsers, networks, operating systems, or hardware.
- Correctness of GitHub Copilot SDK, Copilot CLI, JSON-RPC transport, hooks, session persistence, or authentication.
- Cryptographic implementation correctness.
- Side-channel or covert-channel freedom.
- Information flows not represented by explicit modeled provenance.
- Availability of cloud services.
- Real-world success of remediation or compensation.
- Concurrent distributed races outside the modeled state transition.
- Malicious privileged administrators.
- Arbitrary TypeScript outside the restricted verified module.
- lemmafit editor hooks or daemon behavior.
- Completeness, termination, budget compliance, or success of the Copilot reason-act-observe loop.

## 12. Counterexamples and negative properties

The proof and test suite MUST reject:

- A step with an undeclared capability.
- A tool whose trusted registry capability differs from the step.
- A self-referential or forward dependency.
- A production write without matching approval.
- A production delete even with approval.
- An approval for modified arguments.
- An expired, revoked, consumed, or wrong-role approval.
- A denied step transitioning to execution.
- A second execution using the same idempotency key.
- Any execution transition while the kill switch is active.
- Confidential or restricted modeled data sent to a public-untrusted destination.
- An expired plan.
- A plan containing unknown security-relevant fields.
- A verification result for a different plan digest.

## 13. Differential conformance testing

For each generated test case:

1. Serialize one canonical input.
2. Evaluate it with the executable TypeScript validator.
3. Evaluate the equivalent Dafny model or generated oracle.
4. Assert equal decision and normalized reason category.

The corpus MUST include:

- Boundary values.
- Empty and maximum-sized plans.
- Every enumeration value.
- Every valid state transition.
- Every invalid state transition.
- Randomized dependency graphs.
- Mutated approvals.
- Classification lattice combinations.
- Unknown and malformed schema values.

A mismatch MUST fail CI and invalidate the verification claim for that build.

The runtime conformance suite MUST also assert that:

- The Copilot pre-tool hook and gateway compute the same action digest.
- A hook rejection prevents handler entry.
- A gateway rejection prevents external side effects even if the hook allowed.
- Unknown, malformed, or unavailable hook decisions fail closed.
- Model-declared completion cannot change verified workflow state.

## 14. Property-based and model-based testing

Formal proof is supplemented by:

- Property-based generation of plans and approvals.
- State-machine transition testing.
- Parser fuzzing.
- Canonicalization and digest tests.
- Mutation testing of proof-relevant guards.
- Tool-gateway integration tests.

Tests MUST demonstrate that removal or inversion of each critical guard causes a failure.

## 15. CI and release gates

The verification pipeline MUST:

1. Pin LemmaScript, Dafny, solver, Node.js, and package versions.
2. Validate the supported TypeScript subset.
3. Generate the formal model.
4. Run all required proof obligations.
5. Run differential tests.
6. Run property and negative tests.
7. Generate the guarantee report.
8. Record source, specification, translator, verifier, and solver versions.
9. Bind proof artifacts to the source commit and module digest.

The release MUST fail when:

- A required proof does not verify.
- The verifier times out or returns unknown.
- Differential results disagree.
- A required negative test is accepted.
- The generated guarantee report is missing.
- Artifact binding does not match the release source.

## 16. Runtime use of verification artifacts

At runtime:

- The plan is canonicalized.
- The plan validator returns a decision and plan digest.
- The runtime checks allowed verifier/specification versions.
- The trusted action envelope includes the decision, digest, and versions.
- AGT policy requires `verified` for write-capable actions.
- Any mismatch, unknown version, or indeterminate decision results in deny.

Runtime MUST NOT accept a human-readable guarantee report as authorization evidence.

## 17. Guarantee report

Every build MUST produce a report containing:

- Source commit and module digest.
- Specification version.
- LemmaScript, Dafny, solver, and runtime versions.
- Each invariant and proof status.
- Assumptions.
- Explicit exclusions.
- Differential-test summary.
- Known warnings or unsupported constructs.
- Timestamp and build identity.

The customer-facing console SHOULD show a concise subset with a link to the complete report.

## 18. lemmafit experiment

lemmafit MAY verify the governance console's pure transition logic:

- Denied cannot become executing.
- Approval is required before production execution.
- Completed cannot execute again.
- Revocation invalidates pending execution.
- Kill switch blocks new execution transitions.

The experiment MUST remain separate from runtime authorization unless it meets the same artifact binding, differential testing, CI, and fail-closed requirements as the primary plan gate.

## 19. Verification acceptance criteria

- `VA-01`: All proof obligations `PO-01` through `PO-10` pass for the release build.
- `VA-02`: Every required counterexample is rejected.
- `VA-03`: Differential testing reports no mismatches.
- `VA-04`: Unsupported language features fail verification rather than being ignored.
- `VA-05`: A changed plan invalidates the prior verification result.
- `VA-06`: A verifier timeout or unknown result prevents write execution.
- `VA-07`: The guarantee report lists all assumptions and exclusions.
- `VA-08`: At least one deliberate invariant-breaking mutation fails the release pipeline.
- `VA-09`: Runtime policy denies a write without a matching verified result.
- `VA-10`: Customer-facing language does not claim whole-agent formal verification.
- `VA-11`: Hook and gateway canonicalization agree for the required action corpus.
- `VA-12`: Denial at either enforcement point produces no external side effect.

## 20. Review requirements

Before implementation, this specification requires review by:

- Agent/application architect.
- Security architect.
- Formal-methods reviewer.
- Incident-response domain representative.
- Product owner responsible for customer-facing claims.

Any material change to tool capabilities, plan schema, approval semantics, data classifications, delegation, concurrency, or write operations requires re-review of this specification.
