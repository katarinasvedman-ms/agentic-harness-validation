using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.AI.AgentServer.Invocations;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Core.Serialization;
using GovernedAgent.Host.Workflow;
using GovernedAgent.Simulator;
using Microsoft.Extensions.Options;

namespace GovernedAgent.Host.Hosted;

public sealed class GovernedInvocationHandler(
    IAgentWorkflow workflow,
    IWorkflowSuspensionStore suspensions,
    TrustedToolMetadata trustedTools,
    IOptions<GovernedHostedAgentOptions> configuredOptions) : InvocationHandler
{
    private const string UserIdHeader = "x-agent-user-id";
    private readonly GovernedHostedAgentOptions _options = configuredOptions.Value;

    public override async Task HandleAsync(
        HttpRequest request,
        HttpResponse response,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        context.ClientHeaders.TryGetValue(UserIdHeader, out var userId);
        userId ??= context.PlatformContext.UserIdKey;
        if (string.IsNullOrWhiteSpace(userId))
        {
            await WriteErrorAsync(
                response,
                "missing_platform_user",
                $"The {UserIdHeader} platform header is required.",
                cancellationToken);
            return;
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(
                request.Body,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            await WriteMalformedRequestAsync(response, cancellationToken);
            return;
        }

        using (document)
        {
            if (!TryGetAction(document.RootElement, out var action))
            {
                await WriteMalformedRequestAsync(response, cancellationToken);
                return;
            }

            try
            {
                switch (action)
                {
                    case "execute":
                        await ExecuteAsync(
                            document.RootElement,
                            userId,
                            response,
                            context,
                            cancellationToken);
                        return;
                    case "resume":
                        await ResumeAsync(
                            document.RootElement,
                            userId,
                            response,
                            context,
                            cancellationToken);
                        return;
                    default:
                        await WriteMalformedRequestAsync(response, cancellationToken);
                        return;
                }
            }
            catch (JsonException)
            {
                await WriteMalformedRequestAsync(response, cancellationToken);
            }
        }
    }

    private async Task ExecuteAsync(
        JsonElement body,
        string userId,
        HttpResponse response,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        var externalRequest = body.Deserialize<ExecuteInvocationRequest>(ContractJson.Options);
        if (externalRequest is null || !IsStructurallyValid(externalRequest))
        {
            await WriteMalformedRequestAsync(response, cancellationToken);
            return;
        }

        if (!IsConfiguredPlan(externalRequest.Plan) ||
            !HasValidResourceBindings(externalRequest.Plan) ||
            !HasValidDemoIdentifiers(externalRequest))
        {
            await WriteInvalidWorkflowRequestAsync(response, cancellationToken);
            return;
        }

        var workflowRequest = new AgentWorkflowRequest(
            externalRequest.Plan,
            externalRequest.StepId,
            new UserIdentity(userId, _options.UserRoles),
            new AgentIdentity(
                _options.AgentId,
                _options.AgentIdentity,
                _options.DeploymentVersion),
            new SessionIdentity(
                CreateGovernanceSessionId(userId, externalRequest.Plan.IncidentId),
                externalRequest.Plan.IncidentId),
            externalRequest.IdempotencyKey,
            externalRequest.ExpectedResourceVersion,
            externalRequest.CompletionCriteria);

        AgentWorkflowResult result;
        try
        {
            result = await workflow.ExecuteAsync(workflowRequest, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            await WriteInvalidWorkflowRequestAsync(response, cancellationToken);
            return;
        }
        catch (ArgumentException)
        {
            await WriteInvalidWorkflowRequestAsync(response, cancellationToken);
            return;
        }

        string? resumeToken = null;
        if (result.Status == AgentWorkflowStatus.ApprovalRequired &&
            result.Suspension is not null)
        {
            try
            {
                resumeToken = suspensions.Store(userId, result.Suspension);
            }
            catch (WorkflowSuspensionCapacityException)
            {
                await WriteErrorAsync(
                    response,
                    "suspension_capacity_reached",
                    "The hosted agent cannot safely retain another suspended workflow.",
                    cancellationToken,
                    StatusCodes.Status503ServiceUnavailable);
                return;
            }
            catch (WorkflowSuspensionExpiredException)
            {
                await WriteErrorAsync(
                    response,
                    "suspension_expired",
                    "The workflow plan expired before it could be suspended.",
                    cancellationToken);
                return;
            }
        }

        await WriteSuccessAsync(response, context, result, resumeToken, cancellationToken);
    }

    private async Task ResumeAsync(
        JsonElement body,
        string userId,
        HttpResponse response,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        var resumeRequest = body.Deserialize<ResumeInvocationRequest>(ContractJson.Options);
        if (resumeRequest is null ||
            !string.Equals(resumeRequest.Action, "resume", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(resumeRequest.ResumeToken) ||
            string.IsNullOrWhiteSpace(resumeRequest.ApprovalNonce))
        {
            await WriteMalformedRequestAsync(response, cancellationToken);
            return;
        }

        await using var lease = await suspensions.AcquireAsync(
            resumeRequest.ResumeToken,
            userId,
            cancellationToken);
        if (lease is null)
        {
            await WriteErrorAsync(
                response,
                "suspension_not_found",
                "The resume token is invalid, already consumed, or belongs to another user.",
                cancellationToken);
            return;
        }

        AgentWorkflowResult result;
        try
        {
            try
            {
                result = await workflow.ResumeAsync(
                    lease.Suspension,
                    resumeRequest.ApprovalNonce,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lease.Retain();
                throw;
            }
        }
        catch (KeyNotFoundException)
        {
            await WriteInvalidWorkflowRequestAsync(response, cancellationToken);
            return;
        }
        catch (ArgumentException)
        {
            await WriteInvalidWorkflowRequestAsync(response, cancellationToken);
            return;
        }

        string? resumeToken = null;
        if (result.Status == AgentWorkflowStatus.ApprovalRequired &&
                 result.Suspension is not null)
        {
            lease.Replace(result.Suspension);
            resumeToken = resumeRequest.ResumeToken;
        }
        else if (ShouldRetainSuspension(result))
        {
            lease.Retain();
            resumeToken = resumeRequest.ResumeToken;
        }

        await WriteSuccessAsync(response, context, result, resumeToken, cancellationToken);
    }

    private string CreateGovernanceSessionId(string userId, string incidentId)
    {
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(_options.GovernanceSessionKey));
        var input = Encoding.UTF8.GetBytes(
            $"{_options.GovernanceSessionNamespace.Length}:{_options.GovernanceSessionNamespace}" +
            $"{userId.Length}:{userId}{incidentId.Length}:{incidentId}");
        return $"gov-{Convert.ToHexString(hmac.ComputeHash(input)).ToLowerInvariant()}";
    }

    private bool IsConfiguredPlan(ActionPlan plan) =>
        string.Equals(plan.AgentId, _options.AgentId, StringComparison.Ordinal) &&
        string.Equals(
            plan.DeploymentVersion,
            _options.DeploymentVersion,
            StringComparison.Ordinal);

    private bool HasValidResourceBindings(ActionPlan plan)
    {
        foreach (var step in plan.Steps)
        {
            if (!HasValidResourceBinding(step.Tool, step.Resource, step.Arguments))
            {
                return false;
            }

            if (step.Compensation is not null &&
                !HasValidResourceBinding(
                    step.Compensation.Tool,
                    step.Resource,
                    step.Compensation.Arguments))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasValidResourceBinding(
        string tool,
        ResourceReference resource,
        IReadOnlyDictionary<string, JsonElement> arguments)
    {
        if (!trustedTools.ResourceBindings.TryGetValue(tool, out var binding) ||
            !string.Equals(resource.Type, binding.ResourceType, StringComparison.Ordinal) ||
            !arguments.TryGetValue(binding.ResourceArgument, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return string.Equals(value.GetString(), resource.Id, StringComparison.Ordinal);
    }

    private static bool IsStructurallyValid(ExecuteInvocationRequest request) =>
        string.Equals(request.Action, "execute", StringComparison.Ordinal) &&
        request.Plan is not null &&
        request.Plan.Steps is not null &&
        request.Plan.Steps.All(step =>
            step is not null &&
            step.Resource is not null &&
            step.DataSources is not null &&
            step.Destination is not null &&
            step.Arguments is not null &&
            step.DependsOn is not null) &&
        !string.IsNullOrWhiteSpace(request.StepId) &&
        !string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
        request.CompletionCriteria is not null;

    private static bool HasValidDemoIdentifiers(ExecuteInvocationRequest request)
    {
        if (!string.Equals(
                request.Plan.IncidentId,
                IncidentSimulator.DemoIncidentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.CompletionCriteria.IncidentId,
                IncidentSimulator.DemoIncidentId,
                StringComparison.Ordinal) ||
            (request.CompletionCriteria.ServiceId is not null &&
             !string.Equals(
                 request.CompletionCriteria.ServiceId,
                 IncidentSimulator.DemoServiceId,
                 StringComparison.Ordinal)))
        {
            return false;
        }

        foreach (var step in request.Plan.Steps)
        {
            if (string.Equals(step.Resource.Type, "service", StringComparison.Ordinal) &&
                !string.Equals(
                    step.Resource.Id,
                    IncidentSimulator.DemoServiceId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(step.Resource.Type, "incident", StringComparison.Ordinal) &&
                !string.Equals(
                    step.Resource.Id,
                    IncidentSimulator.DemoIncidentId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (!HasValidArgument(step.Arguments, "incidentId", IncidentSimulator.DemoIncidentId) ||
                !HasValidArgument(step.Arguments, "serviceId", IncidentSimulator.DemoServiceId) ||
                !HasValidInstance(step.Arguments))
            {
                return false;
            }

            if (step.Compensation is not null &&
                (!HasValidArgument(
                    step.Compensation.Arguments,
                    "incidentId",
                    IncidentSimulator.DemoIncidentId) ||
                 !HasValidArgument(
                    step.Compensation.Arguments,
                    "serviceId",
                    IncidentSimulator.DemoServiceId) ||
                 !HasValidInstance(step.Compensation.Arguments)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ShouldRetainSuspension(AgentWorkflowResult result) =>
        result.Status == AgentWorkflowStatus.Failed &&
        result.GatewayResult is null &&
        result.ErrorCategory is
            ErrorCategory.ApprovalInvalid or
            ErrorCategory.PolicyDenied or
            ErrorCategory.PolicyUnavailable or
            ErrorCategory.BudgetExceeded or
            ErrorCategory.KillSwitchActive or
            ErrorCategory.AuditUnavailable;

    private static bool HasValidArgument(
        IReadOnlyDictionary<string, JsonElement> arguments,
        string name,
        string expected) =>
        !arguments.TryGetValue(name, out var value) ||
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool HasValidInstance(
        IReadOnlyDictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("instanceId", out var value))
        {
            return true;
        }

        return value.ValueKind == JsonValueKind.String &&
            value.GetString() is "payments-api-01" or "payments-api-02" or "payments-api-03";
    }

    private static bool TryGetAction(JsonElement body, out string? action)
    {
        action = null;
        if (body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("action", out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        action = property.GetString();
        return action is not null;
    }

    private static Task WriteSuccessAsync(
        HttpResponse response,
        InvocationContext context,
        AgentWorkflowResult result,
        string? resumeToken,
        CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(
            response.Body,
            new GovernedInvocationResponse(
                context.InvocationId,
                context.SessionId,
                resumeToken,
                result),
            ContractJson.Options,
            cancellationToken);
    }

    private static Task WriteMalformedRequestAsync(
        HttpResponse response,
        CancellationToken cancellationToken) =>
        WriteErrorAsync(
            response,
            "malformed_protocol_input",
            "The invocation body must match the strict execute or resume contract.",
            cancellationToken);

    private static Task WriteInvalidWorkflowRequestAsync(
        HttpResponse response,
        CancellationToken cancellationToken) =>
        WriteErrorAsync(
            response,
            "invalid_workflow_request",
            "The workflow request contains an invalid configured identity or demo resource.",
            cancellationToken);

    private static Task WriteErrorAsync(
        HttpResponse response,
        string code,
        string detail,
        CancellationToken cancellationToken,
        int statusCode = StatusCodes.Status400BadRequest)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/problem+json";
        return response.WriteAsJsonAsync(
            new ProtocolError(code, detail),
            ContractJson.Options,
            cancellationToken);
    }

    private sealed record ProtocolError(string Code, string Detail);
}

public sealed record ExecuteInvocationRequest(
    string Action,
    ActionPlan Plan,
    string StepId,
    string IdempotencyKey,
    long ExpectedResourceVersion,
    WorkflowCompletionCriteria CompletionCriteria);

public sealed record ResumeInvocationRequest(
    string Action,
    string ResumeToken,
    string ApprovalNonce);

public sealed record GovernedInvocationResponse(
    string InvocationId,
    string SessionId,
    string? ResumeToken,
    AgentWorkflowResult Workflow);
