using System.Text.Json;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Core.Serialization;
using GovernedAgent.Governance;

namespace GovernedAgent.Simulator;

public sealed class SimulatorGovernedToolExecutor(
    IIncidentSimulator simulator) : IGovernedToolExecutor
{
    public void Validate(PlanStep step, long expectedResourceVersion)
    {
        ArgumentNullException.ThrowIfNull(step);

        switch (step.Tool)
        {
            case "get_incident":
                RequireExactArguments(step.Arguments, "incidentId");
                _ = GetString(step.Arguments, "incidentId");
                break;
            case "query_metrics":
            case "query_logs":
            case "get_service_health":
                RequireExactArguments(step.Arguments, "serviceId");
                _ = GetString(step.Arguments, "serviceId");
                break;
            case "update_incident":
                RequireExactArguments(step.Arguments, "incidentId", "status");
                _ = GetString(step.Arguments, "incidentId");
                var statusText = GetString(step.Arguments, "status");
                if (!Enum.TryParse<IncidentStatus>(
                        statusText,
                        ignoreCase: true,
                        out _))
                {
                    throw InvalidArguments($"Unknown incident status '{statusText}'.");
                }
                EnsureVersion(
                    expectedResourceVersion,
                    simulator.GetIncident(GetString(step.Arguments, "incidentId")).Version);
                break;
            case "restart_service":
                RequireExactArguments(step.Arguments, "serviceId", "instanceId");
                _ = GetString(step.Arguments, "serviceId");
                _ = GetString(step.Arguments, "instanceId");
                EnsureVersion(
                    expectedResourceVersion,
                    simulator.GetServiceHealth(GetString(step.Arguments, "serviceId")).Version);
                break;
            case "restore_service_state":
                RequireExactArguments(
                    step.Arguments,
                    "serviceId",
                    "instanceId",
                    "previousHealth",
                    "sourceVersion");
                _ = GetString(step.Arguments, "serviceId");
                _ = GetString(step.Arguments, "instanceId");
                var healthText = GetString(step.Arguments, "previousHealth");
                if (!Enum.TryParse<ServiceHealth>(
                        healthText,
                        ignoreCase: true,
                        out _))
                {
                    throw InvalidArguments($"Unknown service health '{healthText}'.");
                }
                _ = GetInt64(step.Arguments, "sourceVersion");
                EnsureVersion(
                    expectedResourceVersion,
                    simulator.GetServiceHealth(GetString(step.Arguments, "serviceId")).Version);
                break;
            default:
                throw new GovernanceException(
                    ErrorCategory.UnknownTool,
                    "unknown_tool",
                    $"Tool '{step.Tool}' is not implemented by the simulator.");
        }
    }

    private static void EnsureVersion(long expectedVersion, long actualVersion)
    {
        if (expectedVersion != actualVersion)
        {
            throw new GovernanceException(
                ErrorCategory.Conflict,
                "stale_resource_version",
                $"Expected resource version {expectedVersion}, but found {actualVersion}.");
        }
    }

    public ValueTask<JsonElement> ExecuteAsync(
        PlanStep step,
        string idempotencyKey,
        long expectedResourceVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        cancellationToken.ThrowIfCancellationRequested();

        var result = step.Tool switch
        {
            "get_incident" => Serialize(ExecuteGetIncident(step.Arguments)),
            "query_metrics" => Serialize(ExecuteQueryMetrics(step.Arguments)),
            "query_logs" => Serialize(ExecuteQueryLogs(step.Arguments)),
            "get_service_health" => Serialize(ExecuteGetServiceHealth(step.Arguments)),
            "update_incident" => Serialize(ExecuteUpdateIncident(
                step.Arguments,
                expectedResourceVersion,
                idempotencyKey)),
            "restart_service" => Serialize(ExecuteRestartService(
                step.Arguments,
                expectedResourceVersion,
                idempotencyKey)),
            "restore_service_state" => Serialize(ExecuteRestoreServiceState(
                step.Arguments,
                expectedResourceVersion,
                idempotencyKey)),
            _ => throw new GovernanceException(
                ErrorCategory.UnknownTool,
                "unknown_tool",
                $"Tool '{step.Tool}' is not implemented by the simulator.")
        };

        return ValueTask.FromResult(result);
    }

    private IncidentSnapshot ExecuteGetIncident(
        IReadOnlyDictionary<string, JsonElement> arguments)
    {
        RequireExactArguments(arguments, "incidentId");
        return simulator.GetIncident(GetString(arguments, "incidentId"));
    }

    private IReadOnlyList<MetricSample> ExecuteQueryMetrics(
        IReadOnlyDictionary<string, JsonElement> arguments)
    {
        RequireExactArguments(arguments, "serviceId");
        return simulator.QueryMetrics(GetString(arguments, "serviceId"));
    }

    private IReadOnlyList<LogEntry> ExecuteQueryLogs(
        IReadOnlyDictionary<string, JsonElement> arguments)
    {
        RequireExactArguments(arguments, "serviceId");
        return simulator.QueryLogs(GetString(arguments, "serviceId"));
    }

    private ServiceHealthSnapshot ExecuteGetServiceHealth(
        IReadOnlyDictionary<string, JsonElement> arguments)
    {
        RequireExactArguments(arguments, "serviceId");
        return simulator.GetServiceHealth(GetString(arguments, "serviceId"));
    }

    private SimulatorWriteResult<IncidentSnapshot> ExecuteUpdateIncident(
        IReadOnlyDictionary<string, JsonElement> arguments,
        long expectedVersion,
        string idempotencyKey)
    {
        RequireExactArguments(arguments, "incidentId", "status");
        var statusText = GetString(arguments, "status");
        if (!Enum.TryParse<IncidentStatus>(statusText, ignoreCase: true, out var status))
        {
            throw InvalidArguments($"Unknown incident status '{statusText}'.");
        }

        return simulator.UpdateIncident(
            GetString(arguments, "incidentId"),
            status,
            expectedVersion,
            idempotencyKey);
    }

    private SimulatorWriteResult<ServiceStateCheckpoint> ExecuteRestartService(
        IReadOnlyDictionary<string, JsonElement> arguments,
        long expectedVersion,
        string idempotencyKey)
    {
        RequireExactArguments(arguments, "serviceId", "instanceId");
        return simulator.RestartService(
            GetString(arguments, "serviceId"),
            GetString(arguments, "instanceId"),
            expectedVersion,
            idempotencyKey);
    }

    private SimulatorWriteResult<ServiceHealthSnapshot> ExecuteRestoreServiceState(
        IReadOnlyDictionary<string, JsonElement> arguments,
        long expectedVersion,
        string idempotencyKey)
    {
        RequireExactArguments(
            arguments,
            "serviceId",
            "instanceId",
            "previousHealth",
            "sourceVersion");
        var healthText = GetString(arguments, "previousHealth");
        if (!Enum.TryParse<ServiceHealth>(healthText, ignoreCase: true, out var health))
        {
            throw InvalidArguments($"Unknown service health '{healthText}'.");
        }

        return simulator.RestoreServiceState(
            new ServiceStateCheckpoint(
                GetString(arguments, "serviceId"),
                GetString(arguments, "instanceId"),
                health,
                GetInt64(arguments, "sourceVersion")),
            expectedVersion,
            idempotencyKey);
    }

    private static JsonElement Serialize<T>(T value) =>
        JsonSerializer.SerializeToElement(value, ContractJson.Options);

    private static string GetString(
        IReadOnlyDictionary<string, JsonElement> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw InvalidArguments($"Argument '{name}' must be a string.");
        }

        return value.GetString()
            ?? throw InvalidArguments($"Argument '{name}' cannot be null.");
    }

    private static long GetInt64(
        IReadOnlyDictionary<string, JsonElement> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value) ||
            !value.TryGetInt64(out var result))
        {
            throw InvalidArguments($"Argument '{name}' must be an integer.");
        }

        return result;
    }

    private static void RequireExactArguments(
        IReadOnlyDictionary<string, JsonElement> arguments,
        params string[] expected)
    {
        if (arguments.Count != expected.Length ||
            expected.Any(name => !arguments.ContainsKey(name)))
        {
            throw InvalidArguments(
                $"Arguments must contain exactly: {string.Join(", ", expected)}.");
        }
    }

    private static GovernanceException InvalidArguments(string message) =>
        new(ErrorCategory.Validation, "invalid_tool_arguments", message);
}
