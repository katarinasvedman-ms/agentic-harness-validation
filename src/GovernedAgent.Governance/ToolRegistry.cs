using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Governance;

public interface IToolRegistry
{
    bool TryGet(string toolName, out ToolMetadata metadata);
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, ToolMetadata> _tools;

    public ToolRegistry(IEnumerable<ToolMetadata>? tools = null)
    {
        var registrations = tools?.ToArray() ?? CreateDefaultRegistrations();
        _tools = registrations.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
    }

    public bool TryGet(string toolName, out ToolMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return _tools.TryGetValue(toolName, out metadata!);
    }

    private static ToolMetadata[] CreateDefaultRegistrations() =>
    [
        ReadTool("get_incident", "incident.read"),
        ReadTool("query_metrics", "telemetry.metrics.read"),
        ReadTool("query_logs", "telemetry.logs.read"),
        ReadTool("get_service_health", "service.health.read"),
        WriteTool("update_incident", "incident.update", ApprovalClass.PolicyDependent),
        WriteTool("restart_service", "service.restart", ApprovalClass.IncidentCommander),
        WriteTool(
            "restore_service_state",
            "service.restore",
            ApprovalClass.IncidentCommander)
    ];

    private static ToolMetadata ReadTool(string name, string capability) =>
        Tool(
            name,
            capability,
            EffectKind.Read,
            ApprovalClass.None,
            DataClassification.Confidential);

    private static ToolMetadata WriteTool(
        string name,
        string capability,
        ApprovalClass approvalClass) =>
        Tool(
            name,
            capability,
            EffectKind.Write,
            approvalClass,
            DataClassification.Internal);

    private static ToolMetadata Tool(
        string name,
        string capability,
        EffectKind effect,
        ApprovalClass approvalClass,
        DataClassification maximumClassification) =>
        new(
            Name: name,
            Version: "1.0",
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
