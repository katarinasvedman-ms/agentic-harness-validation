using GovernedAgent.Simulator;

namespace GovernedAgent.UnitTests;

public sealed class IncidentSimulatorTests
{
    [Fact]
    public void SeededScenarioContainsDegradedInstanceAndUntrustedInjection()
    {
        var simulator = new IncidentSimulator();

        var health = simulator.GetServiceHealth(IncidentSimulator.DemoServiceId);
        var logs = simulator.QueryLogs(IncidentSimulator.DemoServiceId);

        Assert.Equal(ServiceHealth.Degraded, health.Health);
        Assert.Contains(
            health.Instances,
            instance =>
                instance.InstanceId == "payments-api-03" &&
                instance.Health == ServiceHealth.Degraded);
        Assert.Contains(
            logs,
            entry =>
                entry.ContainsUntrustedContent &&
                entry.Message == IncidentSimulator.InjectedInstruction);
    }

    [Fact]
    public void RestartIsIdempotentAndRestorable()
    {
        var simulator = new IncidentSimulator();
        var initial = simulator.GetServiceHealth(IncidentSimulator.DemoServiceId);

        var restart = simulator.RestartService(
            IncidentSimulator.DemoServiceId,
            "payments-api-03",
            initial.Version,
            "restart-1");
        var replay = simulator.RestartService(
            IncidentSimulator.DemoServiceId,
            "payments-api-03",
            initial.Version,
            "restart-1");

        Assert.False(restart.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(restart.Version, replay.Version);
        Assert.Equal(
            ServiceHealth.Healthy,
            simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);

        var restored = simulator.RestoreServiceState(
            restart.Value,
            restart.Version,
            "restore-1");

        Assert.Equal(ServiceHealth.Degraded, restored.Value.Health);
    }

    [Fact]
    public void StaleWriteIsRejected()
    {
        var simulator = new IncidentSimulator();
        var initial = simulator.GetIncident(IncidentSimulator.DemoIncidentId);
        simulator.UpdateIncident(
            initial.IncidentId,
            IncidentStatus.Mitigating,
            initial.Version,
            "update-1");

        var error = Assert.Throws<SimulatorConcurrencyException>(() =>
            simulator.UpdateIncident(
                initial.IncidentId,
                IncidentStatus.Resolved,
                initial.Version,
                "update-2"));

        Assert.True(error.ActualVersion > error.ExpectedVersion);
    }

    [Fact]
    public void ResetReturnsToDeterministicInitialState()
    {
        var simulator = new IncidentSimulator();
        var initial = simulator.GetServiceHealth(IncidentSimulator.DemoServiceId);
        simulator.RestartService(
            IncidentSimulator.DemoServiceId,
            "payments-api-03",
            initial.Version,
            "restart-1");

        simulator.Reset();
        var reset = simulator.GetServiceHealth(IncidentSimulator.DemoServiceId);

        Assert.Equal(1, reset.Version);
        Assert.Equal(ServiceHealth.Degraded, reset.Health);
        Assert.All(reset.Instances, instance => Assert.Equal(0, instance.RestartCount));
    }
}
