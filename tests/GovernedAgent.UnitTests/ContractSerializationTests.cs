using System.Text.Json;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Core.Serialization;

namespace GovernedAgent.UnitTests;

public sealed class ContractSerializationTests
{
    [Fact]
    public void ActionPlanSerializationMatchesVersionOneGoldenContract()
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["instance"] = JsonSerializer.SerializeToElement("payments-api-03")
        };

        var plan = new ActionPlan(
            "1.0",
            Guid.Parse("2f64eb2b-40e7-4493-a102-e6fc01828226"),
            "INC-1042",
            "incident-agent",
            "1.0.0",
            DateTimeOffset.Parse("2026-07-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-01T10:05:00Z"),
            [
                new PlanStep(
                    "step-1",
                    "service.restart",
                    "restart_service",
                    new ResourceReference(
                        "service",
                        "payments-api",
                        TargetEnvironment.Production,
                        DataClassification.Internal),
                    [new DataSourceReference("payments-api-metrics", DataClassification.Internal)],
                    new DestinationReference(
                        "payments-api",
                        DataClassification.InternalTrusted),
                    arguments,
                    [],
                    EffectKind.Write,
                    ApprovalClass.IncidentCommander,
                    new CompensationAction("restore_service_state", arguments))
            ]);

        var actual = JsonSerializer.Serialize(plan, ContractJson.Options);
        var expected = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Golden",
            "ActionPlan.v1.json")).Trim();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UnknownSecurityRelevantFieldsAreRejected()
    {
        const string json = """
            {
              "schemaVersion": "1.0",
              "planId": "2f64eb2b-40e7-4493-a102-e6fc01828226",
              "incidentId": "INC-1042",
              "agentId": "incident-agent",
              "deploymentVersion": "1.0.0",
              "createdAt": "2026-07-01T10:00:00+00:00",
              "expiresAt": "2026-07-01T10:05:00+00:00",
              "steps": [],
              "modelSuppliedAuthorization": true
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ActionPlan>(json, ContractJson.Options));
    }
}
