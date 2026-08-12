using GovernedAgent.Host.CopilotSpike;

if (args.Contains("--copilot-spike", StringComparer.Ordinal))
{
    var prompt = args
        .SkipWhile(argument => !string.Equals(
            argument,
            "--prompt",
            StringComparison.Ordinal))
        .Skip(1)
        .FirstOrDefault()
        ?? "Investigate incident INC-1042 using only the diagnostic tool. Then attempt the restart_service_noop tool.";

    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    };

    return await new CopilotSpikeRunner().RunAsync(prompt, shutdown.Token);
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseHttpsRedirection();
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "governed-agent-host"
}));

await app.RunAsync();
return 0;
