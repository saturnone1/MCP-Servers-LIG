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
    .WithTools<GitLabTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-gitlab", gitlab = Guard.BaseUrl }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class GitLabTools
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromHours(1) };

    [McpServerTool(ReadOnly = true)]
    [Description("Return this MCP server's GitLab target configuration.")]
    public static object Config() => new { baseUrl = Guard.BaseUrl, hasToken = !string.IsNullOrWhiteSpace(Guard.Token) };

    [McpServerTool(ReadOnly = true)]
    [Description("List GitLab projects visible to the configured token.")]
    public static Task<ApiResult> ListProjects(string? search = null, int page = 1, int perPage = 100)
    {
        var query = new Dictionary<string, string>
        {
            ["page"] = Math.Max(page, 1).ToString(),
            ["per_page"] = Math.Clamp(perPage, 1, 100).ToString(),
            ["simple"] = "true"
        };
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        return Send(HttpMethod.Get, "/api/v4/projects", query);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get a GitLab project by numeric id or URL-encoded path.")]
    public static Task<ApiResult> GetProject(string project) => Send(HttpMethod.Get, "/api/v4/projects/" + EncodeProject(project));

    [McpServerTool(ReadOnly = true)]
    [Description("List issues in a GitLab project.")]
    public static Task<ApiResult> ListIssues(string project, string state = "opened", int page = 1, int perPage = 100) =>
        Send(HttpMethod.Get, $"/api/v4/projects/{EncodeProject(project)}/issues", new Dictionary<string, string>
        {
            ["state"] = state,
            ["page"] = Math.Max(page, 1).ToString(),
            ["per_page"] = Math.Clamp(perPage, 1, 100).ToString()
        });

    [McpServerTool]
    [Description("Create an issue in a GitLab project.")]
    public static Task<ApiResult> CreateIssue(string project, string title, string? description = null, string[]? labels = null)
    {
        Guard.RequireWrites();
        var body = new Dictionary<string, object?> { ["title"] = title };
        if (!string.IsNullOrWhiteSpace(description)) body["description"] = description;
        if (labels is { Length: > 0 }) body["labels"] = string.Join(",", labels);
        return Send(HttpMethod.Post, $"/api/v4/projects/{EncodeProject(project)}/issues", body: body);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List merge requests in a GitLab project.")]
    public static Task<ApiResult> ListMergeRequests(string project, string state = "opened", int page = 1, int perPage = 100) =>
        Send(HttpMethod.Get, $"/api/v4/projects/{EncodeProject(project)}/merge_requests", new Dictionary<string, string>
        {
            ["state"] = state,
            ["page"] = Math.Max(page, 1).ToString(),
            ["per_page"] = Math.Clamp(perPage, 1, 100).ToString()
        });

    [McpServerTool(ReadOnly = true)]
    [Description("Read a repository file from a GitLab project.")]
    public static Task<ApiResult> GetFile(string project, string filePath, string reference = "main", bool raw = false)
    {
        var suffix = raw ? "/raw" : "";
        return Send(HttpMethod.Get, $"/api/v4/projects/{EncodeProject(project)}/repository/files/{EncodePath(filePath)}{suffix}", new Dictionary<string, string> { ["ref"] = reference });
    }

    [McpServerTool]
    [Description("Create or update a repository file in a GitLab project.")]
    public static async Task<ApiResult> CreateOrUpdateFile(string project, string filePath, string branch, string content, string commitMessage)
    {
        Guard.RequireWrites();
        var path = $"/api/v4/projects/{EncodeProject(project)}/repository/files/{EncodePath(filePath)}";
        var body = new Dictionary<string, object?>
        {
            ["branch"] = branch,
            ["content"] = content,
            ["commit_message"] = commitMessage
        };
        var update = await Send(HttpMethod.Put, path, body: body);
        if (update.StatusCode != 404) return update;
        return await Send(HttpMethod.Post, path, body: body);
    }

    private static async Task<ApiResult> Send(HttpMethod method, string path, Dictionary<string, string>? query = null, object? body = null)
    {
        Uri? uri = null;
        try
        {
            uri = BuildUri(path, query);
            using var request = new HttpRequestMessage(method, uri);
            if (!string.IsNullOrWhiteSpace(Guard.Token))
                request.Headers.Add("PRIVATE-TOKEN", Guard.Token);
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
            return new ApiResult(0, false, $"GitLab request failed for {uri?.ToString() ?? Guard.BaseUrl}: {ex.Message}");
        }
    }

    private static Uri BuildUri(string path, Dictionary<string, string>? query)
    {
        var builder = new UriBuilder(new Uri(new Uri(Guard.BaseUrl), path));
        if (query is { Count: > 0 })
            builder.Query = string.Join("&", query.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return builder.Uri;
    }

    private static string EncodeProject(string value) => Uri.EscapeDataString(value);
    private static string EncodePath(string value) => Uri.EscapeDataString(value);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal static class Guard
{
    public static string BaseUrl => (Environment.GetEnvironmentVariable("GITLAB_BASE_URL") ?? "http://gitlab.local").TrimEnd('/') + "/";
    public static string? Token => Environment.GetEnvironmentVariable("GITLAB_TOKEN");

    public static void RequireWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_GITLAB_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("GitLab write tools are disabled because MCP_ENABLE_GITLAB_WRITES=false.");
    }
}

public sealed record ApiResult(int StatusCode, bool Success, string Body);
