# Product Requirements Document: Governed Enterprise Agent Demo

| Field | Value |
| --- | --- |
| Status | Draft |
| Version | 0.2 |
| Date | 2026-08-12 |
| Product owner | TBD |
| Intended audience | Product, engineering, security, governance, and customer-facing teams |

## 1. Executive summary

The Governed Enterprise Agent Demo will show how an autonomous AI agent can perform useful operational work while remaining subject to deterministic, observable, and enforceable controls.

The initial scenario is an enterprise incident-response agent that investigates a service outage, proposes remediation, requests approval for high-impact actions, and executes only authorized operations. The demo will deliberately expose the agent to prompt injection and unauthorized tool-use attempts to show that safety does not depend on the model consistently following instructions.

The product combines:

- Microsoft Foundry Hosted Agents for managed deployment and operation.
- GitHub Copilot SDK for the inner reason-act-observe agentic loop.
- Microsoft Agent Framework for outer agent composition and workflows.
- Microsoft Agent Governance Toolkit for runtime policy enforcement, identity, audit, and operational controls.
- Microsoft Foundry evaluations and Azure observability for pre-production and production assurance.
- An experimental formal-verification layer for selected deterministic invariants.

The principal message is:

> The model may be probabilistic; the system does not have to be.

## 2. Problem statement

Enterprise customers want agents that can take meaningful action, not only answer questions. Greater autonomy introduces material risks:

- An agent may select the wrong tool or invoke the right tool with unsafe arguments.
- Prompt injection may redirect the agent toward data exfiltration or destructive actions.
- Agent delegation may unintentionally expand privileges.
- Model behaviour may change as prompts, models, tools, or data sources evolve.
- Traditional testing samples behaviour but cannot establish that forbidden actions are impossible.
- Post-incident logs may not explain which agent acted, which policy applied, or why an action was allowed.
- Security and governance controls designed for human-speed processes cannot govern machine-speed actions effectively.

Customers need a concrete architecture demonstrating that useful autonomy and enforceable control can coexist throughout development, deployment, and production.

## 3. Product vision

Create a reusable flagship demo that helps customers understand and evaluate an enterprise-grade approach to agentic systems.

The demo will make the invisible control plane visible. Customers will see the agent reason and act, but also see each authorization decision, approval boundary, proof result, trace, and evaluation outcome.

The product is not intended to claim that a large language model or the complete distributed system is formally verified. It will demonstrate layered assurance:

1. Evaluations measure behavioural quality and detect regressions.
2. Runtime governance deterministically allows, denies, or pauses actions.
3. Formal verification establishes selected properties of bounded deterministic components.
4. Identity and infrastructure permissions enforce least privilege.
5. Observability and audit evidence explain what happened.

## 4. Goals

### 4.1 Primary goals

- Demonstrate an agent completing a credible enterprise incident-response workflow.
- Demonstrate deterministic prevention of unauthorized tool use and unsafe actions.
- Demonstrate human approval for defined high-impact operations.
- Demonstrate least-privilege execution using a distinct agent identity.
- Demonstrate end-to-end traceability from user request to model reasoning, policy decision, approval, tool execution, and outcome.
- Demonstrate evaluations during development, deployment, and production.
- Demonstrate formal verification of selected safety invariants without overstating its coverage.
- Provide a clear architecture that customers can adapt to their own industries and risk profiles.

### 4.2 Secondary goals

- Showcase Microsoft Foundry Hosted Agents and Microsoft Agent Framework together.
- Showcase the GitHub Copilot SDK as a reusable, production-grade agentic loop.
- Showcase the Microsoft Agent Governance Toolkit as an inline runtime control.
- Explore LemmaScript, lemmafit, Dafny, and related verification approaches.
- Show where Rust can add value to high-assurance components.
- Produce reusable demo assets, policies, evaluation datasets, and customer-facing explanations.

## 5. Non-goals

The initial release will not:

- Build a general-purpose enterprise agent platform.
- Provide autonomous remediation across real customer production environments.
- Prove the correctness of the model, prompts, complete agent, or complete distributed system.
- Replace Azure RBAC, network controls, security monitoring, or human accountability.
- Support arbitrary third-party tools or dynamically discovered tools.
- Demonstrate unrestricted multi-agent autonomy.
- Cover every Microsoft Foundry, Agent Framework, or Agent Governance Toolkit capability.
- Depend on experimental formal-verification tooling as the only production security boundary.
- Expose Copilot's built-in shell, filesystem, or unrestricted URL tools to the operational agent.
- Build a second competing agent loop inside Microsoft Agent Framework.
- Optimize for high-scale performance benchmarking.

## 6. Target audiences

### 6.1 Primary audiences

| Audience | Need |
| --- | --- |
| CIO, CTO, and technology leaders | Understand how agent autonomy can be introduced without surrendering control |
| CISO and security architects | See enforceable controls for tool access, identity, data flow, and incident response |
| AI and application architects | Understand the component model and integration boundaries |
| Risk, compliance, and governance teams | See policy evidence, approvals, auditability, and control mapping |
| Engineering leaders | Understand lifecycle validation and operational ownership |

### 6.2 Secondary audiences

- Developers building agents with Microsoft technologies.
- Platform teams operating shared AI capabilities.
- Site reliability and incident-response teams.
- Data protection and privacy stakeholders.

## 7. Personas

### 7.1 Incident operator

An authenticated employee who asks the agent to investigate an outage, reviews evidence, and follows progress.

Needs:

- Fast diagnosis grounded in operational data.
- Clear distinction between evidence and agent inference.
- Visibility into proposed actions and their impact.
- Confidence that the agent cannot silently exceed its authority.

### 7.2 Incident commander

An accountable human who approves or rejects high-impact remediation.

Needs:

- A concise remediation proposal with evidence and risk.
- Assurance that approval applies only to the exact action shown.
- The ability to revoke approval or stop execution.
- A durable record of the decision.

### 7.3 Security and governance reviewer

A stakeholder examining whether the system's controls are effective.

Needs:

- Human-readable policies and proof obligations.
- Evidence of allowed, denied, and approval-required decisions.
- Traceability to agent identity, policy version, and deployment version.
- Demonstration that prompt injection cannot bypass deterministic controls.

### 7.4 Agent developer

An engineer changing prompts, tools, policies, or orchestration.

Needs:

- Fast local feedback.
- Repeatable evaluation and policy regression tests.
- Clear verification failures.
- Deployment gates that prevent unsafe versions from advancing.

## 8. Core demo scenario

### 8.1 Scenario

The Payments API is returning elevated errors. An incident operator asks the Enterprise Operations Agent to investigate and recommend remediation.

The agent can:

- Read incident and service metadata.
- Query simulated logs, metrics, and resource health.
- Correlate evidence and produce a diagnosis.
- Create or update an incident ticket.
- Propose a remediation plan.
- Request approval for a production change.
- Execute an approved, narrowly scoped remediation.
- Verify the outcome and summarize the incident.

### 8.2 Legitimate path

1. The operator asks the agent to investigate the Payments API.
2. The agent uses read-only diagnostic tools.
3. The agent produces a structured plan to restart an unhealthy service instance.
4. The plan passes structural validation.
5. Runtime policy classifies the action as a production write requiring approval.
6. The incident commander reviews and approves the exact action.
7. The governed tool executes with the agent's least-privilege identity.
8. The agent verifies recovery using read-only tools.
9. The system displays the complete trace and audit evidence.

### 8.3 Adversarial path

A diagnostic document or tool response contains an injected instruction directing the agent to upload confidential diagnostics to an untrusted destination.

The demo must show:

1. The model may propose the unsafe action.
2. The verified plan gate rejects a prohibited confidential-source-to-public-sink flow when the action appears in a structured plan.
3. Runtime governance denies any direct unauthorized tool invocation.
4. Infrastructure permissions prevent unrestricted access as an independent control.
5. The denial records the agent identity, action, policy version, and reason.
6. Repeated violations can reduce trust, open a circuit breaker, or activate a kill switch.

## 9. User experience principles

- **Controls must be visible.** The audience should see why an action was allowed, denied, or paused.
- **Evidence before assertion.** Diagnoses and remediation proposals must cite the operational evidence used.
- **No simulated certainty.** The UI must distinguish model confidence, evaluation scores, policy decisions, and formal guarantees.
- **Exact approvals.** Approval must bind to a specific action, arguments, resource, environment, and expiry.
- **Fail closed.** Missing identity, invalid policy, unavailable verification, or expired approval must not result in tool execution.
- **Progressive disclosure.** Executives should understand the story immediately, while technical audiences can inspect traces, policies, and guarantees.
- **Honest assurance.** Formal-verification claims must state their scope, assumptions, and exclusions.

## 10. Product capabilities

### 10.1 Agent investigation

The product must:

- Accept a natural-language incident request.
- Maintain an incident-scoped session.
- Use only registered diagnostic and remediation tools.
- Return evidence-linked findings.
- Produce a structured remediation plan before performing a write operation.

### 10.2 Governed tool execution

The product must:

- Intercept every tool invocation before execution.
- Enforce policy first in the Copilot SDK pre-tool hook and again at the governed tool gateway.
- Treat Copilot permission prompts as interaction mechanics, not enterprise authorization evidence.
- Associate every invocation with authenticated user, agent, session, deployment, and incident identities.
- Apply versioned policy to the action and its arguments.
- Return one of three outcomes: allow, deny, or require approval.
- Prevent denied actions from reaching the underlying tool.
- Fail closed when policy evaluation cannot complete.
- Record the decision and reason.

### 10.3 Human approval

The product must:

- Require approval for configured high-impact actions.
- Show the proposed action, target, arguments, evidence, expected impact, and rollback strategy.
- Bind approval to an immutable action digest.
- Reject altered, expired, reused, or revoked approvals.
- Allow an authorized approver to reject the request.

### 10.4 Identity and least privilege

The product must:

- Execute as a distinct Microsoft Entra agent identity.
- Grant the identity only the permissions required for the demo.
- Keep read and write capabilities separately governable.
- Prevent policy configuration from granting permissions absent from infrastructure identity.

### 10.5 Verification

The product must:

- Define a small set of explicit, reviewable safety invariants.
- Verify selected deterministic, effect-free plan-validation logic.
- Block deployment when required proofs fail.
- Publish a human-readable guarantee report.
- Identify assumptions, unverified components, and semantic gaps.
- Use differential tests where executable code and the formal model may differ.

Candidate invariants include:

- Every plan step uses a declared capability.
- A production write cannot be authorized without valid approval.
- A denied action cannot transition to execution.
- A completed action cannot execute twice.
- A kill switch prevents new execution.
- Confidential data cannot flow to a public destination through modeled plan steps.
- Delegation cannot increase the delegated agent's capabilities.

### 10.6 Evaluation

The product must support:

- Local or development evaluation before deployment.
- Deployment gates based on defined evaluation thresholds.
- Adversarial cases covering prompt injection and tool misuse.
- Regression comparison between agent versions.
- Production sampling or continuous evaluation.
- Correlation between evaluation results and traces.

Evaluation categories should include:

- Task completion.
- Diagnostic groundedness.
- Correct tool selection.
- Tool argument correctness.
- Policy compliance.
- Approval compliance.
- Prompt-injection resistance.
- Recovery and rollback behaviour.

### 10.7 Observability and audit

The product must expose:

- End-to-end distributed traces.
- Copilot agent turns and reliable `session.idle` lifecycle events.
- Model, tool, verification, policy, approval, and execution spans.
- Latency, token usage, tool failures, denials, and approval metrics.
- Agent and deployment version correlation.
- A durable decision record suitable for demonstration and investigation.
- A view that explains which control stopped an unsafe action.

### 10.8 Operational safety

The product must provide:

- A kill switch.
- Circuit breakers for repeated failures or policy violations.
- Action and cost budgets.
- Timeouts and bounded retries.
- Idempotency for write operations.
- A safe demo reset mechanism.

## 11. Demo experience

The default customer presentation should fit within 20 minutes.

### Act 1: Useful autonomy

The agent investigates an outage, correlates evidence, and proposes remediation.

### Act 2: Controlled execution

The audience sees the plan validated, the policy decision made, human approval requested, and the operation executed.

### Act 3: Adversarial challenge

A prompt-injection attempt causes the model to propose an unauthorized action. The control plane blocks it and explains why.

### Act 4: Lifecycle assurance

The presenter shows:

- The evaluation suite used before deployment.
- A comparison between two agent versions.
- The formal guarantee report.
- The production trace and audit evidence.
- The kill switch or circuit-breaker response.

## 12. Success metrics

### 12.1 Product success

- At least 90% of target customer audiences can correctly explain the distinction between evaluation, governance, verification, and infrastructure authorization after the demo.
- At least 80% of customer discussions identify one applicable workload or control pattern.
- The complete scripted demo succeeds in at least 19 of 20 consecutive rehearsals.
- The default demo completes within 20 minutes.

### 12.2 Safety success

- 100% of test cases using undeclared tools are denied before tool execution.
- 100% of modeled production write actions without valid approval are denied.
- 100% of altered, expired, revoked, or replayed approvals are rejected.
- 100% of required formal proof obligations pass before deployment.
- 100% of required audit fields are present for governed actions.
- No demo identity has permissions beyond the documented least-privilege set.
- Kill-switch activation prevents new governed actions within the defined service-level objective.

### 12.3 Quality success

- The agent meets the agreed task-completion threshold on the curated incident dataset.
- No new agent version deploys when a critical policy-compliance evaluation regresses.
- Every demonstrated action can be correlated across trace, policy decision, approval, and audit record.

Exact thresholds not defined in this PRD must be established in the FRD and evaluation specification.

## 13. Scope

### 13.1 Minimum viable demo

- One Foundry Hosted Agent.
- One incident-response scenario using simulated or isolated operational systems.
- Read-only diagnostics and one reversible remediation action.
- Agent Framework orchestration.
- GitHub Copilot SDK reason-act-observe loop.
- Agent Governance Toolkit policy enforcement.
- Human approval for the remediation.
- Entra identity and least-privilege Azure authorization.
- Application Insights tracing.
- A focused Foundry evaluation suite.
- A governance console.
- At least three formally specified invariants.
- One prompt-injection scenario.
- One kill-switch or circuit-breaker demonstration.

### 13.2 Future extensions

- Multiple specialized agents with constrained delegation.
- Cryptographic agent-to-agent identity and trust.
- Additional industries such as financial operations, healthcare, and supply chain.
- Rust or Verus implementation of high-assurance runtime components.
- Broader information-flow verification.
- Policy authoring and simulation experience.
- Automated evidence packs for compliance frameworks.
- Private-network deployment variant.

## 14. Technology direction and constraints

The initial implementation is expected to use:

- Microsoft Foundry Hosted Agents.
- GitHub Copilot SDK for the inner tool-use loop.
- Microsoft Agent Framework with .NET for outer composition.
- Microsoft Agent Governance Toolkit for inline runtime governance.
- Microsoft Entra ID and Azure RBAC.
- OpenTelemetry and Application Insights.
- Microsoft Foundry evaluations.
- TypeScript and Dafny for selected verification experiments.
- LemmaScript for annotated TypeScript verification where feasible.
- lemmafit for a bounded greenfield verified state-machine experience, subject to technical validation.

Constraints:

- GitHub Copilot SDK owns its tool-calling loop; Agent Framework middleware alone cannot enforce custom-tool approval.
- A custom Copilot `OnPreToolUse` hook replaces the default approval hook, so it must explicitly preserve deny and ask behavior.
- The Copilot CLI child runtime, non-interactive authentication, session isolation, and trace propagation inside Foundry Hosted Agents require a feasibility gate before implementation.
- `session.idle` means the Copilot loop has stopped processing; it does not establish business success or policy approval.
- Copilot built-in shell, filesystem, and unrestricted URL capabilities are disabled for the operational MVP.
- Agent Governance Toolkit is in public preview and may introduce breaking changes.
- LemmaScript and lemmafit are emerging tools and must not be the sole enforcement mechanism.
- lemmafit is currently optimized for greenfield React and TypeScript applications and effect-free logic.
- LemmaScript may have semantic gaps between TypeScript execution and its generated verification model.
- Microsoft Agent Framework does not currently provide first-class Rust support.
- GitHub Copilot SDK provides a Rust SDK, but the Agent Framework integration used by this design is .NET.
- Hosted-agent regional availability and preview limitations must be validated before implementation.

## 15. Assumptions

- The demo will run in a dedicated Azure environment with no access to customer production data.
- Operational systems will be simulated or isolated.
- Presenters will have stable access to required Azure services.
- A supported non-interactive Copilot SDK authentication mode will be available for the Hosted Agent.
- Customer-facing claims will distinguish generally available, preview, open-source, and experimental components.
- Security policies and formal specifications will receive human review.
- A Foundry-compatible model deployment will be available in the selected region.

## 16. Dependencies

- Microsoft Foundry project and model deployment.
- Azure Container Registry and Hosted Agent support.
- Application Insights or equivalent Azure Monitor resources.
- Microsoft Entra identity and RBAC configuration.
- Agent Framework and Agent Governance Toolkit packages.
- GitHub Copilot SDK and its compatible Copilot CLI runtime.
- Dafny toolchain.
- LemmaScript and lemmafit technical feasibility.
- Evaluation datasets and adversarial test cases.
- Customer-facing UX and demo script.

## 17. Risks and mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Preview components change | Demo instability | Pin versions, isolate adapters, maintain a known-good environment |
| Copilot CLI cannot run reliably in Hosted Agents | Architecture blocked | Require a hosted compatibility spike and retain Agent Framework Harness as fallback |
| Custom hook replaces default approval behavior | Approval bypass | Make hook conformance tests release-blocking and revalidate at the gateway |
| Copilot loop exposes unnecessary built-in tools | Excessive capability | Disable them and register only governed incident-response tools |
| Copilot session state crosses tenant or incident boundaries | Data disclosure | Use incident-scoped storage, isolated paths, retention controls, and leakage tests |
| Formal specification is wrong | Misleading assurance | Require domain and security review; publish assumptions and exclusions |
| Verification scope expands excessively | Delivery delay | Verify only small, deterministic, high-value invariants |
| Too many technologies obscure the story | Reduced customer clarity | Keep one primary path and reveal technical depth progressively |
| Cloud dependency disrupts live demo | Failed presentation | Provide rehearsed reset, deterministic seed data, and recorded fallback |
| Simulated scenario feels artificial | Low customer relevance | Use realistic evidence, terminology, approvals, and operational failure modes |
| Governance policy differs from RBAC | Unexpected allow or deny result | Test both layers and document the effective permission intersection |
| Audit data contains sensitive content | Compliance concern | Redact payloads, minimize collection, and apply retention controls |
| Model or prompt change causes regression | Unsafe or poor behaviour | Gate deployments with versioned evaluations and policy tests |
| Experimental verifier has semantic gaps | Invalid guarantee | Restrict language subset and add differential conformance tests |

## 18. Product decisions

The following decisions are established for the initial release:

- The primary scenario is enterprise incident response.
- The primary deployment model is a Foundry Hosted Agent.
- The primary inner agentic loop is GitHub Copilot SDK.
- Microsoft Agent Framework on .NET is the outer composition and workflow layer.
- Copilot pre-tool hooks and the governed gateway form two mandatory enforcement points.
- Copilot built-in shell, filesystem, and unrestricted URL tools are excluded from the operational MVP.
- Runtime actions are governed outside the model.
- High-impact production actions require exact human approval.
- Formal verification applies only to selected deterministic components.
- Experimental verification tooling supplements, but never replaces, policy enforcement, identity, and RBAC.
- The initial demo favors one polished agent workflow over broad platform functionality.

## 19. Open product questions

The FRD and design process must resolve:

- Which Azure region and model deployment will be used?
- Which supported non-interactive Copilot authentication mode will be used in the Hosted Agent?
- Can the Copilot CLI child runtime satisfy Hosted Agent lifecycle, isolation, and scale-to-zero requirements?
- Which remediation operation is realistic, reversible, and safe for live demonstration?
- Which Agent Governance Toolkit capabilities are sufficiently stable for the first release?
- Which three to five invariants provide the clearest customer value?
- Should the governance console be a standalone web application or embedded demo surface?
- What production-evaluation sampling strategy can be demonstrated without collecting sensitive content?
- What proof and evaluation evidence should be retained, and for how long?
- What recorded fallback is required for offline or degraded customer environments?

## 20. Acceptance criteria

The initial product is accepted when:

1. The legitimate incident workflow completes end to end.
2. The adversarial workflow is blocked before any unauthorized external action.
3. A production remediation cannot execute without valid, exact approval.
4. The agent identity cannot perform prohibited operations even if application controls are bypassed.
5. Every action is traceable to the relevant user, agent, deployment, policy, approval, and incident.
6. Required evaluations and proofs gate deployment.
7. The governance console accurately distinguishes model output, policy decisions, approvals, execution results, and formal guarantees.
8. The kill switch or circuit breaker prevents subsequent actions as designed.
9. A presenter can deliver the complete narrative in 20 minutes.
10. Customer-facing documentation clearly identifies preview and experimental components.
11. Every Copilot tool request is intercepted before execution and revalidated at the gateway.
12. The system distinguishes Copilot loop idleness from successful business completion.

## 21. Follow-on documents

This PRD should be followed by:

1. Functional Requirements Document.
2. Threat model and abuse-case catalogue.
3. Formal verification specification.
4. Evaluation strategy and dataset specification.
5. Architecture decision records.
6. Demo script and presenter guide.

## 22. References

- [Microsoft Agent Framework overview](https://learn.microsoft.com/agent-framework/overview/)
- [GitHub Copilot SDK](https://github.com/github/copilot-sdk)
- [Microsoft Agent Framework integration for GitHub Copilot](https://docs.github.com/copilot/how-tos/copilot-sdk/integrations/microsoft-agent-framework)
- [GitHub Copilot SDK agent loop](https://github.com/github/copilot-sdk/blob/main/docs/features/agent-loop.md)
- [Hosted agents in Foundry Agent Service](https://learn.microsoft.com/azure/foundry/agents/concepts/hosted-agents)
- [Microsoft Agent Governance Toolkit](https://github.com/microsoft/agent-governance-toolkit)
- [Verification Without Inspection](https://annievella.com/posts/verification-without-inspection/)
- [Unleashing the Power of End-User Programmable AI](https://queue.acm.org/detail.cfm?id=3746223)
- [LemmaScript](https://github.com/midspiral/LemmaScript)
- [lemmafit](https://github.com/midspiral/lemmafit)
