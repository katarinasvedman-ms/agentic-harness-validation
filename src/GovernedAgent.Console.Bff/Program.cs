using System.Text.Json.Serialization;
using GovernedAgent.Console.Bff;
using GovernedAgent.Governance;
using GovernedAgent.Simulator;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IIncidentSimulator, IncidentSimulator>();
builder.Services.AddSingleton<IAuditChain, InMemoryAuditChain>();
builder.Services.AddSingleton<IApprovalStore, InMemoryApprovalStore>();
builder.Services.AddSingleton<IKillSwitch, InMemoryKillSwitch>();
builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
builder.Services.AddSingleton<ActionCanonicalizer>();
builder.Services.AddSingleton(ExecutionBudgetLimits.LocalDefault);
builder.Services.AddSingleton<IConsoleWorkflowSnapshotProvider, DemoWorkflowSnapshotProvider>();
builder.Services.AddSingleton<ConsoleState>();

var app = builder.Build();
app.UseExceptionHandler();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "governed-agent-console-bff"
}));
app.MapConsoleApi();
app.Run();

public partial class Program;
