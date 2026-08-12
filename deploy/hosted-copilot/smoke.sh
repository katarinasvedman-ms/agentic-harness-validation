#!/usr/bin/env bash
set -euo pipefail

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

[[ "$(uname -s)" == "Linux" ]] || fail "Linux is required"
[[ "${CI:-}" == "true" ]] || fail "CI must disable interactive/update behavior"
[[ "${COPILOT_AUTO_UPDATE:-}" == "false" ]] || fail "Copilot auto-update must be disabled"
[[ -x "${COPILOT_CLI_PATH:-}" ]] || fail "Pinned Copilot CLI is unavailable"
[[ ! -w "${COPILOT_CLI_PATH}" ]] || fail "Copilot CLI must be immutable at runtime"

copilot_version="$("${COPILOT_CLI_PATH}" --no-auto-update --version)"
[[ "${copilot_version}" == *"1.0.78"* ]] || fail "unexpected Copilot CLI: ${copilot_version}"
node_version="$(node --version)"
[[ "${node_version}" == "v22.22.0" ]] || fail "unexpected Node.js: ${node_version}"
dotnet_version="$(dotnet --version)"
[[ "${dotnet_version}" == "10.0.400" ]] || fail "unexpected .NET SDK: ${dotnet_version}"

node --check src/plan-verifier/dist/cli.js
node deploy/hosted-copilot/lifecycle-smoke.mjs

grep -Fq 'OnPreToolUse' src/GovernedAgent.Host/CopilotSpike/CopilotSpikeConfiguration.cs ||
  fail "pre-tool hook is not configured"
grep -Fq 'AddBuiltIn("*")' src/GovernedAgent.Host/CopilotSpike/CopilotSpikeConfiguration.cs ||
  fail "built-in tools are not excluded"
grep -Fq 'AddMcp("*")' src/GovernedAgent.Host/CopilotSpike/CopilotSpikeConfiguration.cs ||
  fail "MCP tools are not excluded"
grep -Fq 'CancelAfter(MaximumDuration)' src/GovernedAgent.Host/CopilotSpike/CopilotSpikeRunner.cs ||
  fail "wall-clock cancellation budget is absent"
grep -Fq 'MaximumToolCalls' src/GovernedAgent.Host/CopilotSpike/CopilotSpikeRunner.cs ||
  fail "tool-call budget is absent"
grep -Fq 'MaximumAssistantMessages' src/GovernedAgent.Host/CopilotSpike/CopilotSpikeRunner.cs ||
  fail "turn budget is absent"

echo "PASS Linux compatibility smoke"
echo "  ${copilot_version}"
echo "  Node.js ${node_version}; .NET SDK ${dotnet_version}"
echo "  hook/tool restrictions and cancellation budgets present"
echo "  verifier runtime loadable; Copilot child process terminates without login"
