var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseHttpsRedirection();
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "governed-agent-host"
}));

app.Run();
