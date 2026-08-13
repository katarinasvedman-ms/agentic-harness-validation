using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Core.Serialization;
using GovernedAgent.Governance;

namespace GovernedAgent.Host.Verification;

public sealed class NodePlanVerifier(
    string nodeExecutable,
    string verifierCliPath,
    TimeSpan timeout) : IPlanVerifier
{
    private static readonly JsonSerializerOptions VerifierJson = CreateJsonOptions();

    public async ValueTask<PlanVerificationDecision> VerifyAsync(
        PlanVerificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = nodeExecutable,
                Arguments = $"\"{verifierCliPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        try
        {
            if (!process.Start())
            {
                throw Unavailable("The plan verifier process could not start.");
            }
        }
        catch (Win32Exception exception)
        {
            throw new GovernanceException(
                ErrorCategory.VerificationUnavailable,
                "verifier_unavailable",
                "The plan verifier process could not start.",
                exception);
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            var payload = CreatePayload(request);
            await process.StandardInput.WriteAsync(payload.AsMemory(), linkedSource.Token);
            process.StandardInput.Close();
            var outputTask = process.StandardOutput.ReadToEndAsync(linkedSource.Token);
            var errorTask = process.StandardError.ReadToEndAsync(linkedSource.Token);
            await process.WaitForExitAsync(linkedSource.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw Unavailable(
                    $"The plan verifier exited with code {process.ExitCode}: {error}");
            }

            return JsonSerializer.Deserialize<PlanVerificationDecision>(
                output,
                VerifierJson)
                ?? throw Unavailable("The plan verifier returned no decision.");
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new GovernanceException(
                ErrorCategory.Timeout,
                "verifier_timeout",
                "The plan verifier exceeded its execution deadline.");
        }
        catch (JsonException exception)
        {
            throw new GovernanceException(
                ErrorCategory.VerificationUnavailable,
                "verifier_invalid_response",
                "The plan verifier returned an invalid response.",
                exception);
        }
        finally
        {
            TryKill(process);
        }
    }

    private static string CreatePayload(PlanVerificationRequest request)
    {
        var planJson = JsonSerializer.Serialize(request.Plan, ContractJson.Options);
        var context = new
        {
            nowEpochMilliseconds = request.CurrentTime.ToUnixTimeMilliseconds(),
            maximumSteps = request.MaximumSteps,
            agentCapabilities = request.AgentCapabilities,
            toolRegistry = request.ToolRegistry,
            planDigest = request.PlanDigest,
            specificationVersion = request.SpecificationVersion,
            verifierVersion = request.VerifierVersion
        };
        return JsonSerializer.Serialize(new { planJson, context }, VerifierJson);
    }

    private static GovernanceException Unavailable(string message) =>
        new(
            ErrorCategory.VerificationUnavailable,
            "verifier_unavailable",
            message);

    private static void TryKill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        return options;
    }
}
