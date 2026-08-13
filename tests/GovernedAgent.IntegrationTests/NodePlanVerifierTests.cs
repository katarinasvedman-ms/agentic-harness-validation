using System.Text.Json;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Governance;
using GovernedAgent.Host.Verification;

namespace GovernedAgent.IntegrationTests;

public sealed class NodePlanVerifierTests
{
    [Fact]
    public async Task VerifiesPlanThroughNarrowNodeBoundary()
    {
        var verifier = CreateVerifier();
        var request = CreateRequest();

        var decision = await verifier.VerifyAsync(request, CancellationToken.None);

        Assert.Equal(VerificationResult.Verified, decision.Status);
        Assert.Empty(decision.ReasonCodes);
        Assert.Equal(request.PlanDigest, decision.PlanDigest);
    }

    [Fact]
    public async Task RejectsUnsafePlanThroughNarrowNodeBoundary()
    {
        var verifier = CreateVerifier();
        var request = CreateRequest();
        var unsafeStep = request.Plan.Steps[0] with
        {
            Resource = request.Plan.Steps[0].Resource with
            {
                Environment = TargetEnvironment.Production
            },
            Effect = EffectKind.Delete
        };

        var decision = await verifier.VerifyAsync(
            request with { Plan = request.Plan with { Steps = [unsafeStep] } },
            CancellationToken.None);

        Assert.Equal(VerificationResult.Rejected, decision.Status);
        Assert.Contains("production-delete-prohibited", decision.ReasonCodes);
    }

    [Fact]
    public async Task MissingVerifierRuntimeFailsClosed()
    {
        var verifier = new NodePlanVerifier(
            "node-does-not-exist",
            "missing-verifier.js",
            TimeSpan.FromSeconds(1));

        var error = await Assert.ThrowsAsync<GovernanceException>(async () =>
            await verifier.VerifyAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal("verifier_unavailable", error.Code);
    }

    private static NodePlanVerifier CreateVerifier()
    {
        var repositoryRoot = FindRepositoryRoot();
        return new NodePlanVerifier(
            "node",
            Path.Combine(
                repositoryRoot,
                "src",
                "plan-verifier",
                "dist",
                "cli.js"),
            TimeSpan.FromSeconds(5));
    }

    private static PlanVerificationRequest CreateRequest()
    {
        var now = DateTimeOffset.Parse("2026-08-12T10:00:00Z");
        var step = new PlanStep(
            "step-1",
            "diagnostics.metrics.read",
            "query_metrics",
            new ResourceReference(
                "service",
                "payments-api",
                TargetEnvironment.Production,
                DataClassification.Internal),
            [new DataSourceReference(
                "payments-api-metrics",
                DataClassification.Internal)],
            new DestinationReference(
                "payments-api",
                DataClassification.InternalTrusted),
            new Dictionary<string, JsonElement>
            {
                ["serviceId"] = JsonSerializer.SerializeToElement("payments-api")
            },
            [],
            EffectKind.Read,
            ApprovalClass.None,
            null);
        var plan = new ActionPlan(
            "1.0",
            Guid.Parse("2f64eb2b-40e7-4493-a102-e6fc01828226"),
            "INC-1042",
            "incident-agent",
            "1.0.0",
            now.AddMinutes(-1),
            now.AddMinutes(5),
            [step]);
        return new PlanVerificationRequest(
            plan,
            now,
            8,
            ["diagnostics.metrics.read"],
            new Dictionary<string, VerifierToolMetadata>
            {
                ["query_metrics"] = new(
                    "diagnostics.metrics.read",
                    EffectKind.Read,
                    ApprovalClass.None,
                    "serviceId")
            },
            new string('a', 64),
            "1.0",
            "0.1.0");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "package.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
