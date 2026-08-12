# agentic-harness-validation

Reference implementation of a governed enterprise incident-response agent. The
model investigates and proposes actions while deterministic policy, bounded
formal verification, exact approval, and an independent gateway control every
side effect.

## Local development

Prerequisites:

- .NET SDK 10.0.303 or a compatible .NET 10 feature band.
- Node.js 22 or newer.
- PowerShell 7.
- Dafny 4.11.0 for formal-verification work.

```powershell
npm install
dotnet tool restore
pwsh .\scripts\validate.ps1
```

The current foundation builds without Azure resources. Azure and Microsoft
Foundry environment selection is intentionally deferred until the hosted-agent
feasibility phase.

The local Copilot integration spike can be invoked with:

```powershell
dotnet run --project .\src\GovernedAgent.Host -- --copilot-spike
```

On Windows, the current SDK-pinned CLI is blocked by the upstream timestamp
wire-format issue recorded in ADR 0001. The application fails rather than
weakening or bypassing the SDK protocol check.

## Repository structure

- `src/GovernedAgent.Core` - canonical plans, actions, approvals, decisions, and audit contracts.
- `src/GovernedAgent.Governance` - policy, approval, gateway, budgets, and audit controls.
- `src/GovernedAgent.Simulator` - deterministic incident and service-state simulator.
- `src/GovernedAgent.Host` - Microsoft Agent Framework and Copilot inner-loop host.
- `src/GovernedAgent.Console` - React governance console.
- `src/GovernedAgent.Console.Bff` - authenticated console backend.
- `src/plan-verifier` - deterministic TypeScript validator and formal model boundary.
- `tests` - unit, integration, conformance, security, and verifier test suites.

## Documentation

- [Product Requirements Document](docs/PRD.md)
- [Functional Requirements Document](docs/FRD.md)
- [Threat Model](docs/THREAT_MODEL.md)
- [Verification Specification](docs/VERIFICATION_SPEC.md)
- [ADR 0001: GitHub Copilot SDK inner loop](docs/adr/0001-copilot-sdk-inner-loop.md)
- [Standalone solution pitch](docs/pitch.html)