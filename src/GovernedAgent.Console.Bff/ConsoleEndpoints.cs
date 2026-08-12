using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Console.Bff;

public static class ConsoleEndpoints
{
    public static IEndpointRouteBuilder MapConsoleApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapGet("/incidents/{incidentId}", (string incidentId, ConsoleState state) =>
            Read(() => state.GetIncident(incidentId)));
        api.MapGet("/incidents/{incidentId}/timeline", (string incidentId, ConsoleState state) =>
            Read(() => state.GetTimeline(incidentId)));
        api.MapGet("/incidents/{incidentId}/evidence", (string incidentId, ConsoleState state) =>
            Read(() => state.GetEvidence(incidentId)));
        api.MapGet("/incidents/{incidentId}/plan-verification", (
            string incidentId,
            ConsoleState state) => Read(() => state.GetVerification(incidentId)));
        api.MapGet("/incidents/{incidentId}/approvals/pending", (
            string incidentId,
            ConsoleState state) => Read(() => state.GetPending(incidentId)));
        api.MapGet("/audit", (ConsoleState state) => Results.Ok(state.GetAudit()));
        api.MapGet("/controls", (ConsoleState state) => Results.Ok(state.GetControls()));

        api.MapPost("/approvals/{requestId:guid}/approve", (
            Guid requestId,
            ApprovalMutation request,
            HttpContext context,
            ConsoleState state) => MutateApproval(
                requestId, request, ApprovalDecision.Approved, context, state));
        api.MapPost("/approvals/{requestId:guid}/reject", (
            Guid requestId,
            ApprovalMutation request,
            HttpContext context,
            ConsoleState state) => MutateApproval(
                requestId, request, ApprovalDecision.Rejected, context, state));
        api.MapPut("/controls/kill-switch", (
            KillSwitchMutation request,
            HttpContext context,
            ConsoleState state) =>
        {
            var denied = DemoIdentity.Require(
                context.Request,
                DemoIdentity.IncidentCommanderRole,
                DemoIdentity.GovernanceOperatorRole);
            if (denied is not null)
            {
                return denied;
            }

            return ValidateReason(request.Reason, () =>
                Results.Ok(state.SetKillSwitch(request.Active, request.Reason)));
        });
        api.MapPost("/simulator/reset", (HttpContext context, ConsoleState state) =>
        {
            var denied = DemoIdentity.Require(
                context.Request,
                DemoIdentity.IncidentCommanderRole,
                DemoIdentity.GovernanceOperatorRole);
            if (denied is not null)
            {
                return denied;
            }

            state.Reset();
            return Results.NoContent();
        });

        return endpoints;
    }

    private static IResult MutateApproval(
        Guid requestId,
        ApprovalMutation request,
        ApprovalDecision decision,
        HttpContext context,
        ConsoleState state)
    {
        var denied = DemoIdentity.Require(
            context.Request,
            DemoIdentity.IncidentCommanderRole);
        if (denied is not null)
        {
            return denied;
        }

        return ValidateReason(request.Reason, () =>
        {
            try
            {
                return Results.Ok(state.Decide(
                    requestId,
                    decision,
                    DemoIdentity.Current(context),
                    request.Reason));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Approval not found",
                    detail: exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Approval conflict",
                    detail: exception.Message);
            }
        });
    }

    private static IResult Read<T>(Func<T> read)
    {
        try
        {
            return Results.Ok(read());
        }
        catch (KeyNotFoundException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Resource not found",
                detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: exception.Message);
        }
    }

    private static IResult ValidateReason(string? reason, Func<IResult> action)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reason"] = ["Reason must contain between 1 and 500 characters."]
            });
        }

        return action();
    }
}
