using GitHub.Copilot;

namespace GovernedAgent.Host.CopilotSpike;

public sealed class CopilotSpikeRunner
{
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(2);
    private const int MaximumAssistantMessages = 8;
    private const int MaximumToolCalls = 6;

    public async Task<int> RunAsync(string prompt, CancellationToken cancellationToken)
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "governed-agent-demo",
            Guid.NewGuid().ToString("N"));
        var toolState = new CopilotSpikeToolState();

        await using var client = CopilotSpikeConfiguration.CreateClient(baseDirectory);
        var config = CopilotSpikeConfiguration.CreateSession(toolState);

        // Constructing this adapter proves that the direct SDK loop is consumable
        // through the outer Agent Framework abstraction.
        _ = CopilotSpikeConfiguration.AsAgent(client, config);

        await client.StartAsync(cancellationToken);
        await using var session = await client.CreateSessionAsync(config, cancellationToken);
        using var budgetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        budgetCancellation.CancelAfter(MaximumDuration);

        var assistantMessages = 0;
        var toolCalls = 0;
        using var subscription = session.On<SessionEvent>(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    Console.Write(delta.Data?.DeltaContent);
                    break;
                case AssistantMessageEvent:
                    if (Interlocked.Increment(ref assistantMessages) > MaximumAssistantMessages)
                    {
                        budgetCancellation.Cancel();
                    }
                    break;
                case ToolExecutionStartEvent tool:
                    Console.WriteLine($"\n[tool requested: {tool.Data.ToolName}]");
                    if (Interlocked.Increment(ref toolCalls) > MaximumToolCalls)
                    {
                        budgetCancellation.Cancel();
                    }
                    break;
                case SessionErrorEvent error:
                    Console.Error.WriteLine($"\n[session error: {error.Data.Message}]");
                    break;
            }
        });

        try
        {
            await session.SendAndWaitAsync(
                new MessageOptions { Prompt = prompt },
                MaximumDuration,
                budgetCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            await session.AbortAsync(CancellationToken.None);
            Console.Error.WriteLine("\nThe Copilot spike was aborted by cancellation or budget.");
            return 2;
        }
        finally
        {
            await client.StopAsync();
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Tool handlers entered: read={toolState.DiagnosticCalls}, write-noop={toolState.WriteNoOpCalls}");

        return toolState.WriteNoOpCalls == 0 ? 0 : 3;
    }
}
