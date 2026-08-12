using Azure.AI.AgentServer.Core;
using GovernedAgent.Host.CopilotSpike;
using GovernedAgent.Host.Hosted;

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
builder.Services.AddGovernedHostedAgent(builder.Configuration);
var app = builder.Build();
_ = app.Services.GetRequiredService<GovernedAgent.Governance.IApprovalStore>();

app.UseAgentServerCore();
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "governed-agent-host"
}));
app.MapGovernedHostedAgent();

await app.RunAsync();
return 0;

public partial class Program;
