using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Governance;

public interface IToolRegistry
{
    IReadOnlyCollection<ToolMetadata> Tools { get; }

    bool TryGet(string toolName, out ToolMetadata metadata);
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, ToolMetadata> _tools;
    private readonly IReadOnlyCollection<ToolMetadata> _registeredTools;

    public ToolRegistry(IEnumerable<ToolMetadata>? tools = null)
    {
        var registrations = tools?.ToArray() ?? CreateDefaultRegistrations();
        _tools = registrations.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        _registeredTools = registrations;
    }

    public IReadOnlyCollection<ToolMetadata> Tools => _registeredTools;

    public bool TryGet(string toolName, out ToolMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return _tools.TryGetValue(toolName, out metadata!);
    }

    private static ToolMetadata[] CreateDefaultRegistrations() =>
    [
        ReadTool("get_incident", "incident.read", IntentClass.Observe),
        ReadTool("query_metrics", "telemetry.metrics.read", IntentClass.Diagnose),
        ReadTool("query_logs", "telemetry.logs.read", IntentClass.Diagnose),
        ReadTool("get_service_health", "service.health.read", IntentClass.Observe),
        WriteTool(
            "update_incident",
            "incident.update",
            IntentClass.Communicate,
            ApprovalClass.PolicyDependent),
        WriteTool(
            "restart_service",
            "service.restart",
            IntentClass.Remediate,
            ApprovalClass.IncidentCommander),
        WriteTool(
            "restore_service_state",
            "service.restore",
            IntentClass.Remediate,
            ApprovalClass.IncidentCommander)
    ];

    private static ToolMetadata ReadTool(
        string name,
        string capability,
        IntentClass intent) =>
        Tool(
            name,
            capability,
            intent,
            EffectKind.Read,
            ApprovalClass.None,
            DataClassification.Confidential);

    private static ToolMetadata WriteTool(
        string name,
        string capability,
        IntentClass intent,
        ApprovalClass approvalClass) =>
        Tool(
            name,
            capability,
            intent,
            EffectKind.Write,
            approvalClass,
            DataClassification.Internal);

    private static ToolMetadata Tool(
        string name,
        string capability,
        IntentClass intent,
        EffectKind effect,
        ApprovalClass approvalClass,
        DataClassification maximumClassification) =>
        new(
            Name: name,
            Version: "1.0",
            Intent: intent,
            Capability: capability,
            Effect: effect,
            Environments:
            [
                TargetEnvironment.Development,
                TargetEnvironment.Test,
                TargetEnvironment.Production
            ],
            MaximumInputClassification: maximumClassification,
            ApprovalClass: approvalClass,
            InputSchemaDigest: "pending-schema-digest",
            OutputSchemaDigest: "pending-schema-digest");
}
