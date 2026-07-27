using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
builder.Services.AddHttpClient();
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<ConfluenceTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-confluence", confluence = Guard.BaseUrl }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class ConfluenceTools
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromHours(1) };

    [McpServerTool(ReadOnly = true)]
    [Description("Return this MCP server's Confluence target configuration.")]
    public static object Config() => new
    {
        baseUrl = Guard.BaseUrl,
        compatibility = "Confluence Data Center/Server REST API v1 paths. Targets Confluence Server 5.5+ and Data Center 5.6+, including 8.5 LTS, 9.2 LTS/9.2.9, and current 10.x Data Center REST resources.",
        hasBearerToken = !string.IsNullOrWhiteSpace(Guard.BearerToken),
        hasBasicAuth = !string.IsNullOrWhiteSpace(Guard.Username) && !string.IsNullOrWhiteSpace(Guard.ApiToken),
        hasCookie = !string.IsNullOrWhiteSpace(Guard.Cookie),
        writesEnabled = Guard.WritesEnabled
    };

    [McpServerTool(ReadOnly = true)]
    [Description("Get Confluence server/version information where available. Uses modern Server Information first, then the 6.15.8+ troubleshooting fallback.")]
    public static async Task<ApiResult> ServerInfo()
    {
        var result = await Send(HttpMethod.Get, "/rest/api/settings/systemInfo");
        if (result.Success || result.StatusCode is not (404 or 405))
            return result;
        return await Send(HttpMethod.Get, "/rest/troubleshooting/1.0/pre-upgrade/info");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get the current Confluence user using /rest/api/user/current.")]
    public static Task<ApiResult> CurrentUser(string expand = "") =>
        Send(HttpMethod.Get, "/rest/api/user/current", OptionalQuery(new Dictionary<string, string> { ["expand"] = expand }));

    [McpServerTool(ReadOnly = true)]
    [Description("List Confluence spaces using /rest/api/space.")]
    public static Task<ApiResult> ListSpaces(string? spaceKey = null, string? type = null, string? status = null, int start = 0, int limit = 100, string expand = "")
    {
        var query = new Dictionary<string, string>
        {
            ["start"] = Math.Max(0, start).ToString(),
            ["limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["expand"] = expand
        };
        if (!string.IsNullOrWhiteSpace(spaceKey)) query["spaceKey"] = spaceKey;
        if (!string.IsNullOrWhiteSpace(type)) query["type"] = type;
        if (!string.IsNullOrWhiteSpace(status)) query["status"] = status;
        return Send(HttpMethod.Get, "/rest/api/space", OptionalQuery(query));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get one Confluence space by key using /rest/api/space/{spaceKey}.")]
    public static Task<ApiResult> GetSpace(string spaceKey, string expand = "description.plain,homepage") =>
        Send(HttpMethod.Get, "/rest/api/space/" + Uri.EscapeDataString(spaceKey), OptionalQuery(new Dictionary<string, string> { ["expand"] = expand }));

    [McpServerTool(ReadOnly = true)]
    [Description("List Confluence content using /rest/api/content query parameters.")]
    public static Task<ApiResult> ListContent(string? spaceKey = null, string type = "page", string? title = null, string status = "current", int start = 0, int limit = 100, string expand = "space,version")
    {
        var query = new Dictionary<string, string>
        {
            ["type"] = type,
            ["status"] = status,
            ["start"] = Math.Max(0, start).ToString(),
            ["limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["expand"] = expand
        };
        if (!string.IsNullOrWhiteSpace(spaceKey)) query["spaceKey"] = spaceKey;
        if (!string.IsNullOrWhiteSpace(title)) query["title"] = title;
        return Send(HttpMethod.Get, "/rest/api/content", OptionalQuery(query));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Search Confluence content with CQL using /rest/api/content/search.")]
    public static Task<ApiResult> SearchContent(string cql, int start = 0, int limit = 100, string expand = "space,version")
    {
        var query = new Dictionary<string, string>
        {
            ["cql"] = cql,
            ["start"] = Math.Max(0, start).ToString(),
            ["limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["expand"] = expand
        };
        return Send(HttpMethod.Get, "/rest/api/content/search", OptionalQuery(query));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get one Confluence content item by id.")]
    public static Task<ApiResult> GetContent(string id, string expand = "space,version,body.storage,ancestors") =>
        Send(HttpMethod.Get, "/rest/api/content/" + Uri.EscapeDataString(id), OptionalQuery(new Dictionary<string, string> { ["expand"] = expand }));

    [McpServerTool(ReadOnly = true)]
    [Description("List child pages below a Confluence content item.")]
    public static Task<ApiResult> ListChildPages(string parentId, int start = 0, int limit = 100, string expand = "version,space") =>
        Send(HttpMethod.Get, "/rest/api/content/" + Uri.EscapeDataString(parentId) + "/child/page", OptionalQuery(new Dictionary<string, string>
        {
            ["start"] = Math.Max(0, start).ToString(),
            ["limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["expand"] = expand
        }));

    [McpServerTool]
    [Description("Create a Confluence page with storage-format body.")]
    public static Task<ApiResult> CreatePage(string spaceKey, string title, string storageBody, string? parentId = null)
    {
        Guard.RequireWrites();
        var body = PageBody("page", title, spaceKey, storageBody, version: null, parentId);
        return Send(HttpMethod.Post, "/rest/api/content", body: body);
    }

    [McpServerTool]
    [Description("Update a Confluence page. Pass the next version number, usually current version + 1.")]
    public static Task<ApiResult> UpdatePage(string id, string spaceKey, string title, string storageBody, int version, string? parentId = null, bool minorEdit = false)
    {
        Guard.RequireWrites();
        if (version < 2)
            throw new ArgumentOutOfRangeException(nameof(version), "Confluence updates require the next page version number.");
        var body = PageBody("page", title, spaceKey, storageBody, version, parentId, minorEdit, id);
        return Send(HttpMethod.Put, "/rest/api/content/" + Uri.EscapeDataString(id), body: body);
    }

    [McpServerTool]
    [Description("Delete or trash a Confluence content item.")]
    public static Task<ApiResult> DeleteContent(string id, string? status = null)
    {
        Guard.RequireWrites();
        var query = string.IsNullOrWhiteSpace(status) ? null : new Dictionary<string, string> { ["status"] = status };
        return Send(HttpMethod.Delete, "/rest/api/content/" + Uri.EscapeDataString(id), OptionalQuery(query));
    }

    private static Dictionary<string, string>? OptionalQuery(Dictionary<string, string>? query) =>
        query?.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);

    private static Dictionary<string, object?> PageBody(string type, string title, string spaceKey, string storageBody, int? version, string? parentId, bool minorEdit = false, string? id = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["title"] = title,
            ["space"] = new Dictionary<string, object?> { ["key"] = spaceKey },
            ["body"] = new Dictionary<string, object?>
            {
                ["storage"] = new Dictionary<string, object?>
                {
                    ["value"] = storageBody,
                    ["representation"] = "storage"
                }
            }
        };
        if (!string.IsNullOrWhiteSpace(id))
            body["id"] = id;
        if (!string.IsNullOrWhiteSpace(parentId))
            body["ancestors"] = new object[] { new Dictionary<string, object?> { ["id"] = parentId } };
        if (version is not null)
            body["version"] = new Dictionary<string, object?> { ["number"] = version.Value, ["minorEdit"] = minorEdit };
        return body;
    }

    private static async Task<ApiResult> Send(HttpMethod method, string path, Dictionary<string, string>? query = null, object? body = null)
    {
        Uri? uri = null;
        try
        {
            uri = BuildUri(path, query);
            using var request = new HttpRequestMessage(method, uri);
            ApplyAuth(request);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("mcp-confluence/1.0");
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            using var response = await Client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return new ApiResult((int)response.StatusCode, response.IsSuccessStatusCode, text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return new ApiResult(0, false, $"Confluence request failed for {uri?.ToString() ?? Guard.BaseUrl}: {ex.Message}");
        }
    }

    private static Uri BuildUri(string path, Dictionary<string, string>? query)
    {
        var baseUri = new Uri(Guard.BaseUrl);
        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var relativePath = path.TrimStart('/');
        var builder = new UriBuilder(baseUri)
        {
            Path = string.IsNullOrWhiteSpace(basePath) || basePath == "/"
                ? "/" + relativePath
                : basePath + "/" + relativePath
        };
        if (query is { Count: > 0 })
            builder.Query = string.Join("&", query.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return builder.Uri;
    }

    private static void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Guard.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Guard.BearerToken);
        }
        else if (!string.IsNullOrWhiteSpace(Guard.Username) && !string.IsNullOrWhiteSpace(Guard.ApiToken))
        {
            var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guard.Username + ":" + Guard.ApiToken));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
        }
        if (!string.IsNullOrWhiteSpace(Guard.Cookie))
            request.Headers.TryAddWithoutValidation("Cookie", Guard.Cookie);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal static class Guard
{
    public static string BaseUrl
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("CONFLUENCE_BASE_URL");
            return (string.IsNullOrWhiteSpace(configured) ? "http://confluence.local" : configured.Trim()).TrimEnd('/') + "/";
        }
    }
    public static string? Username => Environment.GetEnvironmentVariable("CONFLUENCE_USERNAME");
    public static string? ApiToken => FirstNonEmpty("CONFLUENCE_API_TOKEN", "CONFLUENCE_PASSWORD");
    public static string? BearerToken => FirstNonEmpty("CONFLUENCE_BEARER_TOKEN", "CONFLUENCE_PAT");
    public static string? Cookie => Environment.GetEnvironmentVariable("CONFLUENCE_COOKIE");
    public static bool WritesEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_CONFLUENCE_WRITES"), "false", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    public static void RequireWrites()
    {
        if (!WritesEnabled)
            throw new UnauthorizedAccessException("Confluence write tools are disabled because MCP_ENABLE_CONFLUENCE_WRITES=false.");
    }
}

public sealed record ApiResult(int StatusCode, bool Success, string Body);
