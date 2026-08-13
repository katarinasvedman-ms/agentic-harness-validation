using GovernedAgent.Governance;
using GovernedAgent.Host.Verification;

namespace GovernedAgent.Host.Hosted;

internal static class VerifierToolRegistryFactory
{
    private static readonly IReadOnlyDictionary<string, ToolResourceBinding> ResourceBindings =
        new Dictionary<string, ToolResourceBinding>(StringComparer.Ordinal)
        {
            ["get_incident"] = new("incident", "incidentId"),
            ["query_metrics"] = new("service", "serviceId"),
            ["query_logs"] = new("service", "serviceId"),
            ["get_service_health"] = new("service", "serviceId"),
            ["update_incident"] = new("incident", "incidentId"),
            ["restart_service"] = new("service", "serviceId"),
            ["restore_service_state"] = new("service", "serviceId")
        };

    public static TrustedToolMetadata Create(
        IToolRegistry registry)
    {
        var registeredNames = registry.Tools
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        var missingBindings = registeredNames
            .Except(ResourceBindings.Keys, StringComparer.Ordinal)
            .ToArray();
        var staleBindings = ResourceBindings.Keys
            .Except(registeredNames, StringComparer.Ordinal)
            .ToArray();
        if (missingBindings.Length > 0 || staleBindings.Length > 0)
        {
            throw new InvalidOperationException(
                "Every trusted tool must have exactly one resource binding.");
        }

        var result = new Dictionary<string, VerifierToolMetadata>(
            ResourceBindings.Count,
            StringComparer.Ordinal);
        foreach (var (name, binding) in ResourceBindings)
        {
            if (!registry.TryGet(name, out var tool) ||
                !string.Equals(tool.Name, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The trusted tool registry is inconsistent for '{name}'.");
            }

            result.Add(
                name,
                new VerifierToolMetadata(
                    tool.Capability,
                    tool.Effect,
                    tool.ApprovalClass,
                    binding.ResourceArgument));
        }

        return new TrustedToolMetadata(result, ResourceBindings);
    }
}

public sealed record ToolResourceBinding(
    string ResourceType,
    string ResourceArgument);

public sealed record TrustedToolMetadata(
    IReadOnlyDictionary<string, VerifierToolMetadata> VerifierTools,
    IReadOnlyDictionary<string, ToolResourceBinding> ResourceBindings);
