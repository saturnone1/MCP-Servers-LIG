using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
builder.Services.AddHttpClient();
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<LokiTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-loki", loki = Guard.BaseUrl }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class LokiTools
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(300) };

    [McpServerTool(ReadOnly = true)]
    [Description("Return this MCP server's Loki target configuration.")]
    public static object Config() => new
    {
        baseUrl = Guard.BaseUrl,
        hasBearerToken = !string.IsNullOrWhiteSpace(Guard.BearerToken),
        hasBasicAuth = !string.IsNullOrWhiteSpace(Guard.Username) && !string.IsNullOrWhiteSpace(Guard.Password),
        tenantId = Guard.TenantId
    };

    [McpServerTool(ReadOnly = true)]
    [Description("Check Loki readiness.")]
    public static Task<LokiResult> Ready() => Get("/ready");

    [McpServerTool(ReadOnly = true)]
    [Description("Run an instant LogQL query.")]
    public static Task<LokiResult> Query(string query, string? time = null, int limit = 100, string direction = "backward")
    {
        var parameters = new Dictionary<string, string>
        {
            ["query"] = query,
            ["limit"] = Math.Clamp(limit, 1, 5000).ToString(CultureInfo.InvariantCulture),
            ["direction"] = direction
        };
        if (!string.IsNullOrWhiteSpace(time)) parameters["time"] = time;
        return Get("/loki/api/v1/query", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Run a LogQL range query.")]
    public static Task<LokiResult> QueryRange(string query, string start, string end, string? step = null, int limit = 100, string direction = "backward")
    {
        var parameters = new Dictionary<string, string>
        {
            ["query"] = query,
            ["start"] = start,
            ["end"] = end,
            ["limit"] = Math.Clamp(limit, 1, 5000).ToString(CultureInfo.InvariantCulture),
            ["direction"] = direction
        };
        if (!string.IsNullOrWhiteSpace(step)) parameters["step"] = step;
        return Get("/loki/api/v1/query_range", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Fetch recent Loki log lines for a stream selector.")]
    public static Task<LokiResult> RecentLogs(string selector, int sinceMinutes = 30, int limit = 200, string direction = "backward")
    {
        var end = DateTimeOffset.UtcNow;
        var start = end.AddMinutes(-Math.Clamp(sinceMinutes, 1, 10080));
        return QueryRange(selector, ToUnixNano(start), ToUnixNano(end), null, limit, direction);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Search recent Loki logs by appending a line filter to a stream selector.")]
    public static Task<LokiResult> SearchLogs(string selector, string pattern, int sinceMinutes = 30, int limit = 200, bool regex = false, string direction = "backward")
    {
        var op = regex ? "|~" : "|=";
        var escaped = pattern.Replace("\\", "\\\\").Replace("\"", "\\\"", StringComparison.Ordinal);
        return RecentLogs(selector + " " + op + " \"" + escaped + "\"", sinceMinutes, limit, direction);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List Loki labels.")]
    public static Task<LokiResult> Labels(string? start = null, string? end = null) =>
        Get("/loki/api/v1/labels", Time(start, end));

    [McpServerTool(ReadOnly = true)]
    [Description("List values for a Loki label.")]
    public static Task<LokiResult> LabelValues(string label, string? start = null, string? end = null) =>
        Get("/loki/api/v1/label/" + Uri.EscapeDataString(label) + "/values", Time(start, end));

    [McpServerTool(ReadOnly = true)]
    [Description("Find Loki series matching selectors.")]
    public static Task<LokiResult> Series(string[] match, string? start = null, string? end = null)
    {
        var parameters = Time(start, end);
        for (var i = 0; i < match.Length; i++)
            parameters["match[]" + i.ToString(CultureInfo.InvariantCulture)] = match[i];
        return Get("/loki/api/v1/series", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get Loki index stats for a LogQL stream selector.")]
    public static Task<LokiResult> IndexStats(string query, string? start = null, string? end = null)
    {
        var parameters = Time(start, end);
        parameters["query"] = query;
        return Get("/loki/api/v1/index/stats", parameters);
    }

    private static async Task<LokiResult> Get(string path, Dictionary<string, string>? parameters = null)
    {
        var uri = BuildUri(path, parameters);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyAuth(request);
        using var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return new LokiResult((int)response.StatusCode, response.IsSuccessStatusCode, body);
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

    private static void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Guard.TenantId))
            request.Headers.Add("X-Scope-OrgID", Guard.TenantId);
        if (!string.IsNullOrWhiteSpace(Guard.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Guard.BearerToken);
            return;
        }
        if (!string.IsNullOrWhiteSpace(Guard.Username) && !string.IsNullOrWhiteSpace(Guard.Password))
        {
            var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guard.Username + ":" + Guard.Password));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
        }
    }

    private static Dictionary<string, string> Time(string? start, string? end)
    {
        var parameters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(start)) parameters["start"] = start;
        if (!string.IsNullOrWhiteSpace(end)) parameters["end"] = end;
        return parameters;
    }

    private static string ToUnixNano(DateTimeOffset value) =>
        (value.ToUnixTimeMilliseconds() * 1_000_000L).ToString(CultureInfo.InvariantCulture);
}

internal static class Guard
{
    public static string BaseUrl => (Environment.GetEnvironmentVariable("LOKI_BASE_URL") ?? "http://host.docker.internal:3100").TrimEnd('/') + "/";
    public static string? BearerToken => Environment.GetEnvironmentVariable("LOKI_BEARER_TOKEN");
    public static string? Username => Environment.GetEnvironmentVariable("LOKI_USERNAME");
    public static string? Password => Environment.GetEnvironmentVariable("LOKI_PASSWORD");
    public static string? TenantId => Environment.GetEnvironmentVariable("LOKI_TENANT_ID");
}

public sealed record LokiResult(int StatusCode, bool Success, string Body);

