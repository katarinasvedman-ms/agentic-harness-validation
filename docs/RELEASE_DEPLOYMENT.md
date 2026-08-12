# Credential-free hosted-agent release

The dispatch-only `Hosted agent release` workflow validates the repository,
builds the production container without credentials, and then waits on the
protected GitHub `demo` environment before using GitHub OIDC. It targets an
existing Microsoft Foundry project and never provisions infrastructure.

## GitHub environment setup

Create a repository environment named `demo`. Add at least one required reviewer
under **Settings → Environments → demo → Deployment protection rules** and
disable self-review where organizational policy requires separation of duties.
Keep branch/tag deployment restrictions appropriate for manually selected
commits. Environment approval gates the whole `deploy-demo` job.

Define these as **environment variables**, not secrets:

- `AZURE_CLIENT_ID`: Entra application/client ID with a GitHub environment
  federated credential whose subject is
  `repo:<owner>/<repository>:environment:demo`.
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_ENV_NAME`: isolated azd environment name for the demo deployment.
- `AZURE_LOCATION`
- `AZURE_AI_PROJECT_ENDPOINT`: existing project endpoint.
- `AZURE_AI_MODEL_DEPLOYMENT_NAME`
- `FOUNDRY_NETWORK_POSTURE`: `public`, `selected-networks`, or `private`.
- `COPILOT_AUTH_STRATEGY`: reviewed non-interactive production strategy.
- `COPILOT_AUTH_REVIEWED`: `true` only after that review.
- `APPROVAL_STORE_PROVIDER`: intended durable/shared `IApprovalStore` provider.

The Entra application needs only reviewed deployment-time Azure and Foundry
roles on the existing target. No client secret, publish profile, Azure
credentials JSON, or repository secret is used.

## Release gates and provenance

`release-validation` reruns the offline Foundry scaffold checks, deterministic
build/tests (including security and conformance), formal proofs, guarantee-report
freshness, dependency audit, and the production Docker build. Only then can the
protected deployment job begin.

The deployment preflight intentionally fails before `azd deploy` today. Once its
external provider gate is implemented, the workflow deploys only
`governed-agent-host`, captures structured `azd ai agent show` output, and sends
one dynamic strict `execute` request. The smoke check succeeds only when the
business result is `approvalRequired` and simulator state is unchanged; model or
protocol completion is not business completion.

Every workflow run uploads release-attempt provenance, including validation and
deployment-job outcomes even when validation fails or environment approval is
rejected. It also records source/run identity, policy/proof/report SHA-256
digests, and evaluation-suite intent. It explicitly does not claim an image
signature, hidden registry digest, or agent status when those values were not
available to the non-environment provenance job.

## Readiness

- **ci-release — BLOCKED (not done):** implement and register the selected
  durable/shared `IApprovalStore`, add its provider-specific connectivity check,
  finalize the reviewed non-interactive Copilot authentication strategy, and
  supply the `demo` environment variables and OIDC federated credential above.
  It also lacks a successful reviewer-approved gated workflow run and an image
  signing/attestation provider.
- **azure-deployment — BLOCKED:** `scripts/foundry-preflight.ps1 -Phase Deploy`
  deliberately prevents deployment until the shared approval provider and its
  connectivity check exist. No Azure or azd command was run to validate a live
  deployment while adding this workflow.

Current Microsoft guidance:
[Set up CI/CD for hosted agents with azd](https://learn.microsoft.com/azure/foundry/agents/how-to/set-up-ci-cd-cli)
and
[GitHub Actions OIDC authentication](https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect).
