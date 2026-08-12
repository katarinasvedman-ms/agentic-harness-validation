using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

#pragma warning disable GHCP001 // Explicit fail-closed permission handling is required by the spike.

namespace GovernedAgent.Host.CopilotSpike;

public static class CopilotSpikeConfiguration
{
    public static CopilotClient CreateClient(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        Directory.CreateDirectory(baseDirectory);

        return new CopilotClient(new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty,
            BaseDirectory = baseDirectory,
            UseLoggedInUser = true,
            Environment = new Dictionary<string, string>
            {
                ["CI"] = "true",
                ["COPILOT_AUTO_UPDATE"] = "false"
            },
            Connection = ResolveRuntimeConnection()
        });
    }

    public static SessionConfig CreateSession(CopilotSpikeToolState toolState)
    {
        ArgumentNullException.ThrowIfNull(toolState);

        AIFunction diagnosticTool = CopilotTool.DefineTool(
            toolState.GetDiagnostic,
            new CopilotToolOptions
            {
                Defer = CopilotToolDefer.Never,
                SkipPermission = false
            },
            new AIFunctionFactoryOptions
            {
                Name = CopilotSpikeConstants.DiagnosticTool,
                Description = "Returns bounded read-only evidence for one demonstration incident."
            });

        AIFunction writeNoOpTool = CopilotTool.DefineTool(
            toolState.RestartServiceNoOp,
            new CopilotToolOptions
            {
                Defer = CopilotToolDefer.Never,
                SkipPermission = false
            },
            new AIFunctionFactoryOptions
            {
                Name = CopilotSpikeConstants.WriteNoOpTool,
                Description = "A write-shaped no-op used only to prove pre-tool denial."
            });

        return new SessionConfig
        {
            Streaming = true,
            Tools = [diagnosticTool, writeNoOpTool],
            AvailableTools = new ToolSet()
                .AddCustom(CopilotSpikeConstants.DiagnosticTool)
                .AddCustom(CopilotSpikeConstants.WriteNoOpTool),
            ExcludedTools = new ToolSet()
                .AddBuiltIn("*")
                .AddMcp("*"),
            McpServers = new Dictionary<string, McpServerConfig>(),
            EnableConfigDiscovery = false,
            EnableFileHooks = false,
            EnableHostGitOperations = false,
            EnableSessionStore = false,
            EnableSkills = false,
            EnableOnDemandInstructionDiscovery = false,
            SkipCustomInstructions = true,
            ManageScheduleEnabled = false,
            ToolSearch = new ToolSearchConfig { Enabled = false },
            Memory = new MemoryConfiguration { Enabled = false },
            OnPermissionRequest = (_, _) =>
                Task.FromResult(PermissionDecision.Reject(
                    "Permission requests are denied by the governed host.")),
            Hooks = new SessionHooks
            {
                OnPreToolUse = (input, _) =>
                {
                    try
                    {
                        return Task.FromResult<PreToolUseHookOutput?>(
                            CopilotSpikePolicy.Decide(input.ToolName));
                    }
                    catch (Exception exception)
                    {
                        return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput
                        {
                            PermissionDecision = "deny",
                            PermissionDecisionReason =
                                $"The governance hook failed closed: {exception.GetType().Name}."
                        });
                    }
                }

                #pragma warning restore GHCP001
            }
        };
    }

    public static AIAgent AsAgent(CopilotClient client, SessionConfig sessionConfig)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(sessionConfig);

        return client.AsAIAgent(
            sessionConfig,
            ownsClient: false,
            id: "governed-copilot-spike",
            name: "Governed Copilot Spike",
            description: "Copilot inner loop exposed through Microsoft Agent Framework.");
    }

    private static RuntimeConnection ResolveRuntimeConnection()
    {
        var explicitPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return RuntimeConnection.ForStdio(explicitPath);
        }

        var workspaceNativeRuntime = Path.Combine(
            Environment.CurrentDirectory,
            "node_modules",
            "@github",
            "copilot-win32-x64",
            "copilot.exe");
        if (File.Exists(workspaceNativeRuntime))
        {
            return RuntimeConnection.ForStdio(workspaceNativeRuntime);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var npmEntryPoint = Path.Combine(
            appData,
            "npm",
            "node_modules",
            "@github",
            "copilot",
            "index.js");

        if (File.Exists(npmEntryPoint))
        {
            return RuntimeConnection.ForStdio("node", [npmEntryPoint]);
        }

        throw new InvalidOperationException(
            "No Copilot CLI runtime was found. Install @github/copilot or set COPILOT_CLI_PATH.");
    }
}
