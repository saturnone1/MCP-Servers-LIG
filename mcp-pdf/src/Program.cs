using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");

var settings = PdfSettings.Load();
var runtime = await PdfRuntime.CreateAsync(settings);
builder.Services.AddSingleton(runtime);
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<PdfTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.Lifetime.ApplicationStopping.Register(runtime.Dispose);
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "healthy",
    server = "mcp-pdf",
    runtime = runtime.Health()
}));
app.MapMcp("/mcp");
app.MapMcp("");
await app.RunAsync();
