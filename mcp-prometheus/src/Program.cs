using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
builder.Services.AddHttpClient();
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<PrometheusTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-prometheus", prometheus = Guard.BaseUrl }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class PrometheusTools
{
    private static readonly HttpClient Client = CreateClient();

    [McpServerTool(ReadOnly = true)]
    [Description("Return this MCP server's Prometheus target configuration.")]
    public static object Config() => new { baseUrl = Guard.BaseUrl };

    [McpServerTool(ReadOnly = true)]
    [Description("Check Prometheus readiness.")]
    public static Task<PrometheusResult> Ready() => Get("/-/ready");

    [McpServerTool(ReadOnly = true)]
    [Description("Run an instant Prometheus query.")]
    public static Task<PrometheusResult> Query(string query, string? time = null, int timeoutSeconds = 30)
    {
        var parameters = new Dictionary<string, string> { ["query"] = query };
        if (!string.IsNullOrWhiteSpace(time)) parameters["time"] = time;
        if (timeoutSeconds > 0) parameters["timeout"] = Math.Clamp(timeoutSeconds, 1, 300) + "s";
        return Get("/api/v1/query", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Run a Prometheus range query.")]
    public static Task<PrometheusResult> QueryRange(string query, string start, string end, string step, int timeoutSeconds = 60)
    {
        var parameters = new Dictionary<string, string>
        {
            ["query"] = query,
            ["start"] = start,
            ["end"] = end,
            ["step"] = step,
            ["timeout"] = Math.Clamp(timeoutSeconds, 1, 300) + "s"
        };
        return Get("/api/v1/query_range", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List Prometheus labels.")]
    public static Task<PrometheusResult> Labels(string? start = null, string? end = null, string[]? match = null)
    {
        var parameters = TimeAndMatch(start, end, match);
        return Get("/api/v1/labels", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List values for a Prometheus label.")]
    public static Task<PrometheusResult> LabelValues(string label, string? start = null, string? end = null, string[]? match = null)
    {
        var parameters = TimeAndMatch(start, end, match);
        return Get("/api/v1/label/" + Uri.EscapeDataString(label) + "/values", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List Prometheus targets.")]
    public static Task<PrometheusResult> Targets(string state = "any") =>
        Get("/api/v1/targets", new Dictionary<string, string> { ["state"] = state });

    [McpServerTool(ReadOnly = true)]
    [Description("List Prometheus alerts.")]
    public static Task<PrometheusResult> Alerts() => Get("/api/v1/alerts");

    [McpServerTool(ReadOnly = true)]
    [Description("Find series matching Prometheus selectors.")]
    public static Task<PrometheusResult> Series(string[] match, string? start = null, string? end = null)
    {
        var parameters = TimeAndMatch(start, end, match);
        return Get("/api/v1/series", parameters);
    }

    private static async Task<PrometheusResult> Get(string path, Dictionary<string, string>? parameters = null)
    {
        Uri? uri = null;
        try
        {
            uri = BuildUri(path, parameters);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrWhiteSpace(Guard.BearerToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Guard.BearerToken);
            using var response = await Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            return new PrometheusResult((int)response.StatusCode, response.IsSuccessStatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return new PrometheusResult(0, false, $"Prometheus request failed for {uri?.ToString() ?? Guard.BaseUrl}: {ex.Message}");
        }
    }

    private static Uri BuildUri(string path, Dictionary<string, string>? parameters)
    {
        var builder = new UriBuilder(new Uri(new Uri(Guard.BaseUrl), path));
        if (parameters is { Count: > 0 })
        {
            var parts = new List<string>();
            foreach (var pair in parameters)
            {
                if (string.IsNullOrWhiteSpace(pair.Value)) continue;
                var key = pair.Key.StartsWith("match[]", StringComparison.OrdinalIgnoreCase) ? "match[]" : pair.Key;
                parts.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(pair.Value));
            }
            builder.Query = string.Join("&", parts);
        }
        return builder.Uri;
    }

    private static Dictionary<string, string> TimeAndMatch(string? start, string? end, string[]? match)
    {
        var parameters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(start)) parameters["start"] = start;
        if (!string.IsNullOrWhiteSpace(end)) parameters["end"] = end;
        if (match is { Length: > 0 })
        {
            for (var i = 0; i < match.Length; i++)
                parameters["match[]" + i.ToString(CultureInfo.InvariantCulture)] = match[i];
        }
        return parameters;
    }

    private static HttpClient CreateClient() => new() { Timeout = TimeSpan.FromSeconds(300) };
}

internal static class Guard
{
    public static string BaseUrl => (Environment.GetEnvironmentVariable("PROMETHEUS_BASE_URL") ?? "http://host.docker.internal:9090").TrimEnd('/') + "/";
    public static string? BearerToken => Environment.GetEnvironmentVariable("PROMETHEUS_BEARER_TOKEN");
}

public sealed record PrometheusResult(int StatusCode, bool Success, string Body);
