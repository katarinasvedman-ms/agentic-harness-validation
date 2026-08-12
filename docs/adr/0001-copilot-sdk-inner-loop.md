# ADR 0001: Use GitHub Copilot SDK as the Inner Agentic Loop

| Field | Value |
| --- | --- |
| Status | Proposed; implementation feasibility pending |
| Date | 2026-08-12 |
| Decision owners | Product and architecture team |

## Context

The demo needs an autonomous reason-act-observe loop with multi-turn context, tool invocation, streaming, sessions, skills, MCP support, hooks, and telemetry. Microsoft Agent Framework can provide an agent harness, but GitHub Copilot SDK exposes the production Copilot CLI loop directly and integrates with Agent Framework as an `AIAgent`.

The system also requires deterministic governance before every action. GitHub's integration documentation states that Copilot owns its tool-calling loop. Custom tool approval therefore occurs through Copilot's native pre-tool hook rather than a normal Agent Framework approval round-trip. A custom `OnPreToolUse` hook replaces the default approval hook.

## Decision

Use:

- GitHub Copilot SDK and Copilot CLI as the inner autonomous tool-use loop.
- Microsoft Agent Framework as the outer agent abstraction, workflow, and future multi-agent composition layer.
- Microsoft Foundry Hosted Agents as the managed container, endpoint, identity, isolation, scaling, and observability platform.
- AGT policy evaluation in Copilot `OnPreToolUse`.
- A governed gateway that independently repeats policy, approval, verification, digest, budget, kill-switch, and idempotency checks.

Copilot's built-in shell, filesystem, and unrestricted URL tools are disabled for the operational MVP. Only explicit incident-response tools are registered.

`session.idle` indicates that the inner loop stopped processing. Model-declared `task_complete` is advisory. Successful incident completion is determined by deterministic workflow and outcome state.

## Rationale

- Reuses a GA, production-tested agentic loop rather than building one.
- Keeps Agent Framework available for enterprise composition and provider interoperability.
- Preserves Foundry Hosted Agents as the main customer-facing deployment story.
- Provides native tool interception and detailed turn-level events.
- Supports .NET now and leaves a direct Rust SDK option for later research.
- Keeps governance outside model discretion through dual enforcement.

## Consequences

Positive:

- Less custom orchestration code.
- Strong developer and demo story across GitHub and Microsoft Foundry.
- Explicit visibility into each LLM turn and tool request.
- A clean separation between probabilistic loop and deterministic controls.

Negative:

- A child Copilot CLI runtime must operate correctly inside the Hosted Agent container.
- Authentication and session persistence become additional runtime concerns.
- Governance must integrate with Copilot hooks, not only Agent Framework middleware.
- Duplicate loop ownership must be avoided.
- SDK/CLI version compatibility must be pinned and tested.

## Feasibility gate

Implementation remains blocked until a later spike confirms:

1. Reliable CLI startup, health, cancellation, and shutdown in Hosted Agents.
2. Supported non-interactive authentication.
3. Built-in tool restriction.
4. Pre-execution interception of every custom tool.
5. Gateway-only operational execution.
6. Incident-scoped session isolation and scale-to-zero resume.
7. W3C trace propagation across Foundry, Agent Framework, SDK, CLI, hooks, and tools.
8. Enforceable turn, tool, time, token, and cost budgets.

If the gate fails, use Microsoft Agent Framework Harness as the inner loop while retaining the same tools, gateway, governance, proofs, approval, telemetry, and console.

## Alternatives considered

### Agent Framework Harness only

Simpler runtime and identity story, but does not showcase GitHub Copilot SDK and may require more custom loop behavior.

### Copilot SDK without Agent Framework

Simpler inner architecture, but loses the standard `AIAgent` abstraction and planned enterprise composition story.

### Two nested autonomous loops

Rejected because it risks duplicate tool execution, conflicting state, ambiguous completion, and policy bypass.

## References

- [GitHub Copilot SDK](https://github.com/github/copilot-sdk)
- [Copilot SDK agent loop](https://github.com/github/copilot-sdk/blob/main/docs/features/agent-loop.md)
- [Microsoft Agent Framework integration](https://docs.github.com/copilot/how-tos/copilot-sdk/integrations/microsoft-agent-framework)
- [Microsoft Agent Framework GitHub Copilot integration](https://learn.microsoft.com/agent-framework/integrations/by-component/agent-services/github-copilot)
- [Agentic Loop reference](https://agentic-loop-geguehdxa0c0h4bx.b02.azurefd.net/concepts/platform)
