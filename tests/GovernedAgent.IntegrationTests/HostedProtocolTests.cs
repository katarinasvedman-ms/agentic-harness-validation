using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Core.Serialization;
using GovernedAgent.Governance;
using GovernedAgent.Host.Hosted;
using GovernedAgent.Host.Workflow;
using GovernedAgent.Simulator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GovernedAgent.IntegrationTests;

public sealed class HostedProtocolTests :
    IClassFixture<WebApplicationFactory<HostedAgentEntryPoint>>
{
    private readonly WebApplicationFactory<HostedAgentEntryPoint> _application;
    private readonly HttpClient _client;

    public HostedProtocolTests(
        WebApplicationFactory<HostedAgentEntryPoint> application)
    {
        _application = application;
        _client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/readiness")]
    public async Task ProbeIsReady(string path)
    {
        using var response = await _client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidProductionWriteRequiresApproval()
    {
        using var response = await PostAsync(
            JsonSerializer.Serialize(CreateRequest(), ContractJson.Options),
            includeUser: true);
        var result = await response.Content.ReadFromJsonAsync<GovernedInvocationResponse>(
            ContractJson.Options,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.True(
            result.Workflow.Status == AgentWorkflowStatus.ApprovalRequired,
            result.Workflow.ReasonCode);
        Assert.NotNull(result.Workflow.Suspension);
        Assert.False(string.IsNullOrWhiteSpace(result.ResumeToken));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public async Task MalformedBodyIsBadRequest(string body)
    {
        using var response = await PostAsync(body, includeUser: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExecuteThenResumeCompletesUsingServerSideSuspension()
    {
        using var application = _application.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();
        const string sessionId = "execute-resume-session";
        using var executeResponse = await PostAsync(
            client,
            JsonSerializer.Serialize(CreateRequest(), ContractJson.Options),
            sessionId,
            includeUser: true);
        var suspended = await executeResponse.Content
            .ReadFromJsonAsync<GovernedInvocationResponse>(
                ContractJson.Options,
                CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, executeResponse.StatusCode);
        Assert.NotNull(suspended?.Workflow.Suspension);
        var suspension = suspended.Workflow.Suspension;
        Assert.False(string.IsNullOrWhiteSpace(suspended.ResumeToken));
        const string nonce = "hosted-approval-nonce";
        using (var scope = application.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IApprovalStore>().Add(
                CreateApproval(suspension!, nonce));
        }

        using var resumeResponse = await PostAsync(
            client,
            JsonSerializer.Serialize(
                new ResumeInvocationRequest("resume", suspended.ResumeToken!, nonce),
                ContractJson.Options),
            sessionId,
            includeUser: true);
        var completed = await resumeResponse.Content
            .ReadFromJsonAsync<GovernedInvocationResponse>(
                ContractJson.Options,
                CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        Assert.Equal(AgentWorkflowStatus.Completed, completed?.Workflow.Status);
        Assert.Null(completed?.Workflow.Suspension);
        Assert.Null(completed?.ResumeToken);

        using var replayResponse = await PostAsync(
            client,
            JsonSerializer.Serialize(
                new ResumeInvocationRequest("resume", suspended.ResumeToken!, nonce),
                ContractJson.Options),
            "rotated-correlation-session",
            includeUser: true);
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);
    }

    [Fact]
    public async Task WrongApprovalNonceRetainsSuspensionForValidRetry()
    {
        using var application = _application.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();
        using var executeResponse = await PostAsync(
            client,
            JsonSerializer.Serialize(CreateRequest(), ContractJson.Options),
            "approval-retry-session",
            includeUser: true);
        var pending = (await executeResponse.Content
            .ReadFromJsonAsync<GovernedInvocationResponse>(
                ContractJson.Options,
                CancellationToken.None))!;
        const string validNonce = "valid-after-wrong-nonce";
        using (var scope = application.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IApprovalStore>().Add(
                CreateApproval(pending.Workflow.Suspension!, validNonce));
        }

        using var denied = await PostAsync(
            client,
            JsonSerializer.Serialize(
                new ResumeInvocationRequest(
                    "resume",
                    pending.ResumeToken!,
                    "wrong-nonce"),
                ContractJson.Options),
            "approval-retry-session",
            includeUser: true);
        var failed = await denied.Content
            .ReadFromJsonAsync<GovernedInvocationResponse>(
                ContractJson.Options,
                CancellationToken.None);
        Assert.Equal(AgentWorkflowStatus.Failed, failed?.Workflow.Status);
        Assert.Equal("approval_invalid", failed?.Workflow.ReasonCode);
        Assert.Equal(pending.ResumeToken, failed?.ResumeToken);

        using var accepted = await PostAsync(
            client,
            JsonSerializer.Serialize(
                new ResumeInvocationRequest(
                    "resume",
                    pending.ResumeToken!,
                    validNonce),
                ContractJson.Options),
            "approval-retry-session",
            includeUser: true);
        var completed = await accepted.Content
            .ReadFromJsonAsync<GovernedInvocationResponse>(
                ContractJson.Options,
                CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(AgentWorkflowStatus.Completed, completed?.Workflow.Status);
        Assert.Null(completed?.ResumeToken);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("agent")]
    [InlineData("session")]
    [InlineData("suspension")]
    [InlineData("envelope")]
    public async Task CallerControlledTrustedStateIsRejected(string field)
    {
        var json = JsonSerializer.SerializeToNode(CreateRequest(), ContractJson.Options)!;
        json[field] = new JsonObject();

        using var response = await PostAsync(
            json.ToJsonString(),
            includeUser: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResumeSuspensionInBodyIsRejected()
    {
        var json = JsonSerializer.SerializeToNode(
            new ResumeInvocationRequest("resume", "token", "nonce"),
            ContractJson.Options)!;
        json["suspension"] = new JsonObject();

        using var response = await PostAsync(
            json.ToJsonString(),
            includeUser: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TwoPendingExecutionsCoexistAndResumeIndependently()
    {
        using var application = _application.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();
        var pending = new List<GovernedInvocationResponse>();

        foreach (var item in new[]
                 {
                     (CorrelationId: "shared-caller-session", InstanceId: "payments-api-02"),
                     (CorrelationId: "shared-caller-session", InstanceId: "payments-api-03")
                 })
        {
            using var response = await PostAsync(
                client,
                JsonSerializer.Serialize(CreateRequest(item.InstanceId), ContractJson.Options),
                item.CorrelationId,
                includeUser: true);
            pending.Add((await response.Content.ReadFromJsonAsync<GovernedInvocationResponse>(
                ContractJson.Options,
                CancellationToken.None))!);
        }

        Assert.All(pending, item => Assert.False(string.IsNullOrWhiteSpace(item.ResumeToken)));
        Assert.NotEqual(pending[0].ResumeToken, pending[1].ResumeToken);

        using (var scope = application.Services.CreateScope())
        {
            var approvals = scope.ServiceProvider.GetRequiredService<IApprovalStore>();
            approvals.Add(CreateApproval(pending[0].Workflow.Suspension!, "nonce-one"));
            approvals.Add(CreateApproval(pending[1].Workflow.Suspension!, "nonce-two"));
        }

        for (var index = 0; index < pending.Count; index++)
        {
            using var response = await PostAsync(
                client,
                JsonSerializer.Serialize(
                    new ResumeInvocationRequest(
                        "resume",
                        pending[index].ResumeToken!,
                        $"nonce-{(index == 0 ? "one" : "two")}"),
                    ContractJson.Options),
                $"unrelated-correlation-{index}",
                includeUser: true);
            var completed = await response.Content.ReadFromJsonAsync<GovernedInvocationResponse>(
                ContractJson.Options,
                CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(completed);
            Assert.NotEqual(
                AgentWorkflowStatus.ApprovalRequired,
                completed.Workflow.Status);
        }
    }

    [Fact]
    public async Task ResumeTokenIsBoundToTrustedPlatformUser()
    {
        using var application = _application.WithWebHostBuilder(_ => { });
        using var client = application.CreateClient();
        using var execute = await PostAsync(
            client,
            JsonSerializer.Serialize(CreateRequest(), ContractJson.Options),
            "correlation",
            includeUser: true);
        var pending = (await execute.Content.ReadFromJsonAsync<GovernedInvocationResponse>(
            ContractJson.Options,
            CancellationToken.None))!;

        using var denied = await PostAsync(
            client,
            JsonSerializer.Serialize(
                new ResumeInvocationRequest("resume", pending.ResumeToken!, "nonce"),
                ContractJson.Options),
            "correlation",
            includeUser: true,
            userId: "other-operator");

        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        var error = await denied.Content.ReadFromJsonAsync<ProtocolError>(
            ContractJson.Options,
            CancellationToken.None);
        Assert.Equal("suspension_not_found", error?.Code);
    }

    [Fact]
    public async Task UnknownActionIsRejected()
    {
        using var response = await PostAsync(
            """{"action":"complete"}""",
            includeUser: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingPlatformUserIsRejected()
    {
        using var response = await PostAsync(
            JsonSerializer.Serialize(CreateRequest(), ContractJson.Options),
            includeUser: false);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(
            ContractJson.Options,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("missing_platform_user", error?.Code);
    }

    [Fact]
    public async Task PlatformAndConfiguredIdentitiesAreUsed()
    {
        var workflow = new RequestObservingWorkflow();
        using var application = _application.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IAgentWorkflow>(workflow)));
        using var client = application.CreateClient();

        using var response = await PostAsync(
            client,
            JsonSerializer.Serialize(CreateRequest(), ContractJson.Options),
            "trusted-session",
            includeUser: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = await workflow.Request.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("operator-1", request.User.Id);
        Assert.Equal(["incident-operator"], request.User.Roles);
        Assert.Equal(
            new AgentIdentity("incident-agent", "agent-identity", "1.0.0"),
            request.Agent);
        Assert.StartsWith("gov-", request.Session.Id, StringComparison.Ordinal);
        Assert.Equal(IncidentSimulator.DemoIncidentId, request.Session.IncidentId);
        Assert.DoesNotContain("trusted-session", request.Session.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerSessionRotationDoesNotRotateGovernanceSessionOrBudgetKey()
    {
        var workflow = new RequestObservingWorkflow();
        using var application = _application.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IAgentWorkflow>(workflow)));
        using var client = application.CreateClient();

        foreach (var callerSession in new[] { "caller-session-a", "caller-session-b" })
        {
            using var response = await PostAsync(
                client,
                JsonSerializer.Serialize(CreateRequest(), ContractJson.Options),
                callerSession,
                includeUser: true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var first = await workflow.NextRequestAsync();
        var second = await workflow.NextRequestAsync();
        Assert.Equal(first.Session, second.Session);
        Assert.NotEqual("caller-session-a", first.Session.Id);
        Assert.NotEqual("caller-session-b", second.Session.Id);
    }

    [Theory]
    [InlineData("agentId", "other-agent")]
    [InlineData("deploymentVersion", "2.0.0")]
    [InlineData("incidentId", "INC-9999")]
    public async Task InvalidConfiguredIdentityOrDemoIdIsBadRequest(
        string field,
        string value)
    {
        var json = JsonSerializer.SerializeToNode(CreateRequest(), ContractJson.Options)!;
        ((JsonObject)json["plan"]!)[field] = value;

        using var response = await PostAsync(
            json.ToJsonString(),
            includeUser: true);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(
            ContractJson.Options,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_workflow_request", error?.Code);
    }

    [Theory]
    [InlineData("serviceId", "other-service")]
    [InlineData("instanceId", "payments-api-99")]
    public async Task InvalidDemoToolIdentifierIsStructuredBadRequest(
        string argument,
        string value)
    {
        var json = JsonSerializer.SerializeToNode(CreateRequest(), ContractJson.Options)!;
        var step = (JsonObject)json["plan"]!["steps"]![0]!;
        ((JsonObject)step["arguments"]!)[argument] = value;

        using var response = await PostAsync(
            json.ToJsonString(),
            includeUser: true);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(
            ContractJson.Options,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_workflow_request", error?.Code);
    }

    [Theory]
    [InlineData("resourceType")]
    [InlineData("resourceId")]
    [InlineData("resourceArgumentMismatch")]
    public async Task RestartServiceRequiresTrustedResourceBinding(string mutation)
    {
        var json = JsonSerializer.SerializeToNode(CreateRequest(), ContractJson.Options)!;
        var step = (JsonObject)json["plan"]!["steps"]![0]!;
        var resource = (JsonObject)step["resource"]!;
        var arguments = (JsonObject)step["arguments"]!;
        switch (mutation)
        {
            case "resourceType":
                resource["type"] = "arbitrary";
                break;
            case "resourceId":
                resource["id"] = "arbitrary-service";
                arguments["serviceId"] = "arbitrary-service";
                break;
            default:
                arguments["serviceId"] = "other-service";
                break;
        }

        using var response = await PostAsync(json.ToJsonString(), includeUser: true);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(
            ContractJson.Options,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_workflow_request", error?.Code);
    }

    [Fact]
    public async Task SuspensionStoreExpiresEntriesAndReclaimsCapacity()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryWorkflowSuspensionStore(
            clock,
            capacity: 1,
            maximumTtl: TimeSpan.FromMinutes(1));
        var suspension = CreateSuspension(clock.GetUtcNow().AddSeconds(10));
        var expiredToken = store.Store("operator-1", suspension);

        clock.Advance(TimeSpan.FromSeconds(11));

        Assert.Null(await store.AcquireAsync(
            expiredToken,
            "operator-1",
            CancellationToken.None));
        var replacementToken = store.Store(
            "operator-1",
            CreateSuspension(clock.GetUtcNow().AddMinutes(5)));
        Assert.False(string.IsNullOrWhiteSpace(replacementToken));
    }

    [Fact]
    public async Task SuspensionStoreBoundsPlanLifetimeByMaximumTtl()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryWorkflowSuspensionStore(
            clock,
            capacity: 1,
            maximumTtl: TimeSpan.FromSeconds(5));
        var token = store.Store(
            "operator-1",
            CreateSuspension(clock.GetUtcNow().AddMinutes(5)));

        clock.Advance(TimeSpan.FromSeconds(6));

        Assert.Null(await store.AcquireAsync(
            token,
            "operator-1",
            CancellationToken.None));
    }

    [Fact]
    public void SuspensionStoreFailsClosedAtCapacity()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryWorkflowSuspensionStore(
            clock,
            capacity: 1,
            maximumTtl: TimeSpan.FromMinutes(1));
        store.Store(
            "operator-1",
            CreateSuspension(clock.GetUtcNow().AddMinutes(5)));

        Assert.Throws<WorkflowSuspensionCapacityException>(() =>
            store.Store(
                "operator-1",
                CreateSuspension(clock.GetUtcNow().AddMinutes(5))));
    }

    [Fact]
    public void ProductionRequiresExternallyRegisteredApprovalStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Environments.Production));
        services.AddGovernedHostedAgent();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            provider.GetRequiredService<IApprovalStore>);
        Assert.Contains("externally registered shared", exception.Message);
    }

    [Fact]
    public void PreRegisteredApprovalStoreIsPreservedInProduction()
    {
        var expected = new InMemoryApprovalStore();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Environments.Production));
        services.AddSingleton<IApprovalStore>(expected);
        services.AddGovernedHostedAgent();
        using var provider = services.BuildServiceProvider();

        Assert.Same(expected, provider.GetRequiredService<IApprovalStore>());
    }

    [Fact]
    public void EveryRegisteredToolRequiresTrustedResourceMetadata()
    {
        var defaultRegistry = new ToolRegistry();
        var tools = defaultRegistry.Tools
            .Append(defaultRegistry.Tools.First() with { Name = "unbound_tool" });
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment("Testing"));
        services.AddGovernedHostedAgent();
        services.AddSingleton<IToolRegistry>(new ToolRegistry(tools));
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(
            provider.GetRequiredService<TrustedToolMetadata>);
    }

    [Fact]
    public async Task WorkflowFailureRemainsStructured()
    {
        var request = CreateRequest();
        request = request with
        {
            Plan = request.Plan with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }
        };

        using var response = await PostAsync(
            JsonSerializer.Serialize(request, ContractJson.Options),
            includeUser: true);
        var result = await response.Content.ReadFromJsonAsync<GovernedInvocationResponse>(
            ContractJson.Options,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(AgentWorkflowStatus.Failed, result.Workflow.Status);
        Assert.NotEmpty(result.Workflow.ReasonCode);
    }

    [Fact]
    public async Task RequestCancellationReachesWorkflow()
    {
        var workflow = new CancellationObservingWorkflow();
        using var application = _application.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IAgentWorkflow>(workflow)));
        using var client = application.CreateClient();
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/invocations?agent_session_id=cancellation-session")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(CreateRequest(), ContractJson.Options),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("x-agent-user-id", "operator-1");
        var responseTask = client.SendAsync(request, cancellation.Token);
        await workflow.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await workflow.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await responseTask);
    }

    private Task<HttpResponseMessage> PostAsync(string body, bool includeUser) =>
        PostAsync(
            _client,
            body,
            "protocol-test-session",
            includeUser);

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string body,
        string sessionId,
        bool includeUser,
        string userId = "operator-1")
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/invocations?agent_session_id={sessionId}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (includeUser)
        {
            request.Headers.Add("x-agent-user-id", userId);
        }

        return client.SendAsync(request, CancellationToken.None);
    }

    private static ExecuteInvocationRequest CreateRequest(
        string instanceId = "payments-api-03")
    {
        var now = DateTimeOffset.UtcNow;
        var step = new PlanStep(
            "step-1",
            "service.restart",
            "restart_service",
            new ResourceReference(
                "service",
                IncidentSimulator.DemoServiceId,
                TargetEnvironment.Production,
                DataClassification.Internal),
            [new DataSourceReference("payments-api-metrics", DataClassification.Internal)],
            new DestinationReference(
                IncidentSimulator.DemoServiceId,
                DataClassification.InternalTrusted),
            new Dictionary<string, JsonElement>
            {
                ["serviceId"] = JsonSerializer.SerializeToElement(
                    IncidentSimulator.DemoServiceId),
                ["instanceId"] = JsonSerializer.SerializeToElement(instanceId)
            },
            [],
            EffectKind.Write,
            ApprovalClass.IncidentCommander,
            new CompensationAction(
                "restore_service_state",
                new Dictionary<string, JsonElement>
                {
                    ["serviceId"] = JsonSerializer.SerializeToElement(
                        IncidentSimulator.DemoServiceId),
                    ["instanceId"] = JsonSerializer.SerializeToElement(instanceId),
                    ["previousHealth"] = JsonSerializer.SerializeToElement("degraded"),
                    ["sourceVersion"] = JsonSerializer.SerializeToElement(1)
                }));
        var plan = new ActionPlan(
            "1.0",
            Guid.NewGuid(),
            IncidentSimulator.DemoIncidentId,
            "incident-agent",
            "1.0.0",
            now.AddMinutes(-1),
            now.AddMinutes(5),
            [step]);
        return new ExecuteInvocationRequest(
            "execute",
            plan,
            step.StepId,
            $"protocol-test-{Guid.NewGuid():N}",
            1,
            new WorkflowCompletionCriteria(
                IncidentSimulator.DemoIncidentId,
                ServiceId: IncidentSimulator.DemoServiceId,
                ServiceHealth: ServiceHealth.Healthy));
    }

    private static ApprovalArtifact CreateApproval(
        AgentWorkflowSuspension suspension,
        string nonce)
    {
        var now = DateTimeOffset.UtcNow;
        return new ApprovalArtifact(
            Guid.NewGuid(),
            "commander-1",
            ["incident-commander"],
            suspension.Request.Plan.PlanId,
            suspension.Request.StepId,
            suspension.ActionDigest,
            IncidentSimulator.DemoServiceId,
            TargetEnvironment.Production,
            ApprovalDecision.Approved,
            now.AddMinutes(-1),
            now.AddMinutes(5),
            nonce,
            "1.0");
    }

    private static AgentWorkflowSuspension CreateSuspension(DateTimeOffset expiresAt)
    {
        var external = CreateRequest();
        var request = new AgentWorkflowRequest(
            external.Plan with { ExpiresAt = expiresAt },
            external.StepId,
            new UserIdentity("operator-1", ["incident-operator"]),
            new AgentIdentity("incident-agent", "agent-identity", "1.0.0"),
            new SessionIdentity("session", IncidentSimulator.DemoIncidentId),
            external.IdempotencyKey,
            external.ExpectedResourceVersion,
            external.CompletionCriteria);
        return new AgentWorkflowSuspension(request, null!, "plan", "action");
    }

    private sealed record ProtocolError(string Code, string Detail);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "HostedProtocolTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class CancellationObservingWorkflow : IAgentWorkflow
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<AgentWorkflowResult> ExecuteAsync(
            AgentWorkflowRequest request,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.SetResult();
                throw;
            }

            throw new InvalidOperationException("The infinite delay returned unexpectedly.");
        }

        public ValueTask<AgentWorkflowResult> ResumeAsync(
            AgentWorkflowSuspension suspension,
            string approvalNonce,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RequestObservingWorkflow : IAgentWorkflow
    {
        private readonly Channel<AgentWorkflowRequest> _requests =
            Channel.CreateUnbounded<AgentWorkflowRequest>();

        public Task<AgentWorkflowRequest> Request =>
            _requests.Reader.ReadAsync().AsTask();

        public Task<AgentWorkflowRequest> NextRequestAsync() =>
            _requests.Reader.ReadAsync().AsTask();

        public ValueTask<AgentWorkflowResult> ExecuteAsync(
            AgentWorkflowRequest request,
            CancellationToken cancellationToken)
        {
            _requests.Writer.TryWrite(request);
            return ValueTask.FromResult(new AgentWorkflowResult(
                AgentWorkflowStatus.Failed,
                "observed",
                null,
                null,
                null,
                null,
                ErrorCategory.Validation));
        }

        public ValueTask<AgentWorkflowResult> ResumeAsync(
            AgentWorkflowSuspension suspension,
            string approvalNonce,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
