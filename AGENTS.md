# Governed Agent Demo Engineering Guide

This project was built with the microsoft-foundry skill. Before working on or answering questions about foundry agents, read the microsoft-foundry skill first.

## Engineering constraints

- Keep the local demo runnable without Azure resources.
- Treat model output, Copilot permission prompts, and completion signals as untrusted input.
- Route every side effect through the governed gateway; tool handlers must not call operational systems directly.
- Fail closed when policy, verification, approval, audit, or trusted metadata is unavailable or indeterminate.
- Keep built-in shell, filesystem, unrestricted URL, and unapproved MCP capabilities disabled.
- Preserve matching canonicalization at the pre-tool hook and gateway.
- Do not claim whole-agent formal verification. Proof claims apply only to the bounded deterministic plan model.
- Never run Azure authentication commands for the user or commit environment values and credentials.
- Before any `azd` command, read the Microsoft Foundry `azd-guidance` skill and set `AZURE_DEV_USER_AGENT=microsoft_foundry_skill` for that command only.

## Validation

Run the repository validation command before committing:

```powershell
pwsh .\scripts\validate.ps1
```
