using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Core.Serialization;
using GovernedAgent.Governance;
using GovernedAgent.Simulator;

namespace GovernedAgent.Console.Bff;

public sealed record ConsoleWorkflowSnapshot(
    VerificationView Verification,
    string ActionDigest,
    string RequiredRole,
    string PolicyVersion);

// This boundary can be replaced by the hosted workflow without coupling the BFF to its runtime.
public interface IConsoleWorkflowSnapshotProvider
{
    ConsoleWorkflowSnapshot GetSnapshot(string incidentId);
}

public sealed class DemoWorkflowSnapshotProvider : IConsoleWorkflowSnapshotProvider
{
    private readonly ConsoleWorkflowSnapshot _snapshot;

    public DemoWorkflowSnapshotProvider(
        TimeProvider timeProvider,
        IToolRegistry toolRegistry,
        ActionCanonicalizer canonicalizer)
    {
        _snapshot = CreateSnapshot(timeProvider, toolRegistry, canonicalizer);
    }

    public ConsoleWorkflowSnapshot GetSnapshot(string incidentId)
    {
        if (!string.Equals(
                incidentId,
                IncidentSimulator.DemoIncidentId,
                StringComparison.Ordinal))
        {
            throw new KeyNotFoundException($"Incident '{incidentId}' does not exist.");
        }

        return _snapshot;
    }

    private static ConsoleWorkflowSnapshot CreateSnapshot(
        TimeProvider timeProvider,
        IToolRegistry toolRegistry,
        ActionCanonicalizer canonicalizer)
    {
        var restart = GetTool(toolRegistry, "restart_service");
        var restore = GetTool(toolRegistry, "restore_service_state");
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["serviceId"] = JsonSerializer.SerializeToElement(
                IncidentSimulator.DemoServiceId),
            ["instanceId"] = JsonSerializer.SerializeToElement("payments-api-03")
        };
        var compensationArguments = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal)
        {
            ["serviceId"] = JsonSerializer.SerializeToElement(
                IncidentSimulator.DemoServiceId),
            ["instanceId"] = JsonSerializer.SerializeToElement("payments-api-03"),
            ["previousHealth"] = JsonSerializer.SerializeToElement("degraded"),
            ["sourceVersion"] = JsonSerializer.SerializeToElement(1L)
        };
        var step = new PlanStep(
            "restart-degraded-instance",
            restart.Capability,
            restart.Name,
            new ResourceReference(
                "service",
                IncidentSimulator.DemoServiceId,
                TargetEnvironment.Production,
                DataClassification.Internal),
            [new DataSourceReference("payments-api-metrics", DataClassification.Internal)],
            new DestinationReference(
                IncidentSimulator.DemoServiceId,
                DataClassification.InternalTrusted),
            arguments,
            [],
            restart.Effect,
            restart.ApprovalClass,
            new CompensationAction(restore.Name, compensationArguments));
        var now = timeProvider.GetUtcNow();
        var plan = new ActionPlan(
            "1.0",
            Guid.Parse("77081e67-5487-4c5d-93fe-14ac979914b4"),
            IncidentSimulator.DemoIncidentId,
            "incident-agent",
            "1.0.0",
            now.AddMinutes(-1),
            now.AddMinutes(15),
            [step]);
        var canonicalPlan = CanonicalJson.Serialize(plan);
        var planDigest = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPlan)));
        var actionDigest = canonicalizer.CreateDigest(plan, step).Value;

        // This local adapter represents the verifier attestation shape accepted by the
        // workflow. A production adapter must supply the actual workflow decision.
        var verification = new VerificationView(
            plan,
            VerificationResult.Verified,
            "1.0",
            "0.1.0",
            planDigest,
            ["represented-workflow-attestation"]);
        return new(
            verification,
            actionDigest,
            DemoIdentity.IncidentCommanderRole,
            "1.0");
    }

    private static ToolMetadata GetTool(IToolRegistry registry, string name)
    {
        if (!registry.TryGet(name, out var metadata))
        {
            throw new InvalidOperationException(
                $"Required console workflow tool '{name}' is not registered.");
        }

        return metadata;
    }
}
