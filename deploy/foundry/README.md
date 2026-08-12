# Microsoft Foundry hosted deployment

This is a credential-free container scaffold for Invocations 2.0.0. The
compatibility image in `deploy/hosted-copilot` remains the offline Linux smoke
gate; `deploy/foundry/Dockerfile` is the production runtime image.
Container packaging uses the root `.dockerignore`; `.agentignore` applies to
direct code deployment and is intentionally absent.

## Decisions required before deployment

Set these in the local process or azd environment, never in source:

- `FOUNDRY_PROJECT_MODE`: `existing` or `new`
- `AZURE_AI_PROJECT_ENDPOINT`: existing project endpoint during provision; final
  project endpoint for every deploy
- `AZURE_SUBSCRIPTION_ID`, `AZURE_LOCATION`
- `AZURE_AI_MODEL_DEPLOYMENT_NAME`
- `FOUNDRY_NETWORK_POSTURE`: `public`, `selected-networks`, or `private`
- `COPILOT_AUTH_STRATEGY`: the reviewed non-interactive production strategy
- `COPILOT_AUTH_REVIEWED`: exactly `true` after that production review
- `APPROVAL_STORE_PROVIDER`: the registered durable/shared `IApprovalStore`

The preflight prints no values:

```powershell
./scripts/foundry-preflight.ps1 -Phase Provision
./scripts/foundry-preflight.ps1 -Phase Deploy
```

The provision phase succeeds once its choices are complete. For a new project,
it permits the endpoint to be absent because provision creates it; selecting
`FOUNDRY_PROJECT_MODE=new` is the explicit creation decision. The deploy phase
intentionally remains blocked because this repository has no production
approval provider. Implement and register the selected shared store, then
replace its final gate with a provider-specific connectivity check. The
application also fails closed in Production until that is done.

## Tool prerequisite

The canonical manifest requires `azure.ai.agents >=1.0.0-beta.4`. The
repository's documented local azd 1.25.5 / preview-extension combination cannot
load that generation. Upgrade azd to a version supported by the beta extension,
then install or upgrade `microsoft.foundry`; do not pin the manifest back to the
incompatible preview.

```sh
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd version
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd ext install microsoft.foundry
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd ext upgrade microsoft.foundry
```

## Existing project

After choosing the project and model, populate the deferred environment:

```sh
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd env new
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd env set AZURE_AI_PROJECT_ENDPOINT "$AZURE_AI_PROJECT_ENDPOINT"
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd env set AZURE_AI_MODEL_DEPLOYMENT_NAME "$AZURE_AI_MODEL_DEPLOYMENT_NAME"
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd deploy governed-agent-host --no-prompt
```

The project-level `predeploy` hook automatically runs the deploy preflight.

## New project

Project creation is a separate, explicit infrastructure decision. Select the
subscription, region, model, and network posture first. In a reviewed change,
remove the deferred `endpoint` binding from the `foundry-project` service and
declare the chosen model deployment, then:

```sh
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd env new
# The project-level preprovision hook permits this preview only after the
# subscription, location, model, network posture, and explicit new-project
# choice are configured.
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd provision --preview --no-prompt
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd provision --no-prompt
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd env get-values
AZURE_DEV_USER_AGENT=microsoft_foundry_skill azd deploy governed-agent-host --no-prompt
```

Set the resulting `AZURE_AI_PROJECT_ENDPOINT` before deploy. The project-level
hooks enforce both phases automatically. Authentication is an
operator-controlled prerequisite; this runbook does not automate login. No
command above was run while creating this scaffold.
