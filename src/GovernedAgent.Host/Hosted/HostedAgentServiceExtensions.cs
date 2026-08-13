using Azure.AI.AgentServer.Invocations;
using Azure.AI.AgentServer.Core;
using GovernedAgent.Governance;
using GovernedAgent.Host.Verification;
using GovernedAgent.Host.Workflow;
using GovernedAgent.Simulator;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GovernedAgent.Host.Hosted;

public sealed class HostedAgentEntryPoint;

public sealed class GovernedHostedAgentOptions
{
    public const string LocalDemoGovernanceKey =
        "LOCAL-DEMO-ONLY-NOT-A-PRODUCTION-CREDENTIAL";

    public string AgentId { get; set; } = "incident-agent";

    public string AgentIdentity { get; set; } = "agent-identity";

    public string DeploymentVersion { get; set; } = "1.0.0";

    public string[] UserRoles { get; set; } = ["incident-operator"];

    public string GovernanceSessionKey { get; set; } = LocalDemoGovernanceKey;

    public string GovernanceSessionNamespace { get; set; } = "governed-agent-demo";

    public int SuspensionCapacity { get; set; } = 1_000;

    public TimeSpan MaximumSuspensionTtl { get; set; } = TimeSpan.FromMinutes(15);
}

public static class HostedAgentServiceExtensions
{
    public static IServiceCollection AddGovernedHostedAgent(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddAgentServerCore();
        services.AddInvocationsServer();
        services.AddScoped<InvocationHandler, GovernedInvocationHandler>();
        services.AddHealthChecks();
        IConfiguration hostedConfiguration = configuration is null
            ? new ConfigurationBuilder().Build()
            : configuration.GetSection("GovernedHostedAgent");
        services.AddOptions<GovernedHostedAgentOptions>()
            .Bind(hostedConfiguration)
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.AgentId) &&
                    !string.IsNullOrWhiteSpace(options.AgentIdentity) &&
                    !string.IsNullOrWhiteSpace(options.DeploymentVersion) &&
                    !string.IsNullOrWhiteSpace(options.GovernanceSessionKey) &&
                    !string.IsNullOrWhiteSpace(options.GovernanceSessionNamespace) &&
                    options.UserRoles is { Length: > 0 } &&
                    options.UserRoles.All(role => !string.IsNullOrWhiteSpace(role)) &&
                    options.SuspensionCapacity > 0 &&
                    options.MaximumSuspensionTtl > TimeSpan.Zero,
                "Hosted agent identity and user roles must be configured.")
            .ValidateOnStart();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IWorkflowSuspensionStore>(provider =>
        {
            var options = provider
               .GetRequiredService<IOptions<GovernedHostedAgentOptions>>()
               .Value;
            return new InMemoryWorkflowSuspensionStore(
               provider.GetRequiredService<TimeProvider>(),
               options.SuspensionCapacity,
               options.MaximumSuspensionTtl);
        });

        services.AddSingleton<IToolRegistry>(new ToolRegistry());
        services.AddSingleton(provider => VerifierToolRegistryFactory.Create(
            provider.GetRequiredService<IToolRegistry>()));
        services.AddSingleton<ActionCanonicalizer>();
        services.AddSingleton<IIncidentSimulator, IncidentSimulator>();
        services.AddSingleton<IGovernedToolExecutor, SimulatorGovernedToolExecutor>();
        services.AddSingleton<IPolicyEvaluator, DefaultDenyPolicyEvaluator>();
        services.TryAddSingleton<IApprovalStore>(provider =>
        {
            var environment = provider.GetRequiredService<IHostEnvironment>();
            if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            {
                return new InMemoryApprovalStore();
            }

            throw new InvalidOperationException(
                "Production hosting requires an externally registered shared IApprovalStore.");
        });
        services.AddSingleton<IExecutionBudgetStore>(
            new InMemoryExecutionBudgetStore(ExecutionBudgetLimits.LocalDefault));
        services.AddSingleton<IKillSwitch, InMemoryKillSwitch>();
        services.AddSingleton<IAuditChain, InMemoryAuditChain>();
        services.AddSingleton<GovernedToolGateway>();
        services.AddSingleton<IWorkflowCompletionEvaluator, SimulatorWorkflowCompletionEvaluator>();
        services.AddSingleton<IPlanVerifier>(provider => new NodePlanVerifier(
            Environment.GetEnvironmentVariable("NODE_EXECUTABLE") ?? "node",
            ResolveVerifierCli(provider.GetRequiredService<IHostEnvironment>()),
            TimeSpan.FromSeconds(10)));
        services.AddSingleton<IAgentWorkflow>(provider =>
        {
            var metadata = provider.GetRequiredService<TrustedToolMetadata>();
            var verifierRegistry = metadata.VerifierTools;
            return new LocalDeterministicAgentWorkflow(
                provider.GetRequiredService<IPlanVerifier>(),
                provider.GetRequiredService<ActionCanonicalizer>(),
                provider.GetRequiredService<GovernedToolGateway>(),
                provider.GetRequiredService<IWorkflowCompletionEvaluator>(),
                verifierRegistry.Values
                    .Select(metadata => metadata.Capability)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                verifierRegistry);
        });

        return services;
    }

    private static string ResolveVerifierCli(IHostEnvironment environment)
    {
        var packaged = Path.Combine(
            AppContext.BaseDirectory,
            "Hosted",
            "verifier",
            "cli.js");
        if (File.Exists(packaged))
        {
            return packaged;
        }

        return Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            "..",
            "plan-verifier",
            "dist",
            "cli.js"));
    }

    public static IEndpointRouteBuilder MapGovernedHostedAgent(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/readiness");
        endpoints.MapInvocationsServer();
        return endpoints;
    }
}
