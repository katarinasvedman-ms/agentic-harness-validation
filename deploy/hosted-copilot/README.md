# Linux hosted Copilot compatibility harness

This image reproduces the process boundary expected in a Linux Foundry Hosted
Agent without authenticating or provisioning anything. It pins .NET 10.0.400
(which satisfies the repository's forward-compatible 10.0.303 baseline),
Node.js 22.22.0, Copilot SDK 1.0.9 (through central package management), Agent
Framework integration 1.17.0, and the SDK-matched Linux Copilot CLI 1.0.78.

```sh
docker build -f deploy/hosted-copilot/Dockerfile -t governed-agent-hosted-smoke .
docker run --rm --network none governed-agent-hosted-smoke
```

The build compiles the app and verifier and runs the security and integration
tests on Linux. The runtime smoke requires no credentials or network. It checks
the immutable CLI version, explicit update suppression, stdio child startup and
cancellation cleanup, hook/tool restriction configuration, bounded execution,
and availability of the Node verifier.

This does not prove Hosted Agent authentication, managed identity, scale-to-zero
resume/session behavior, or platform trace propagation. Those require a real
Foundry deployment.
