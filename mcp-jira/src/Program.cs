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
    .WithTools<JiraTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-jira", jira = Guard.BaseUrl }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class JiraTools
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromHours(1) };

    [McpServerTool(ReadOnly = true)]
    [Description("Return this MCP server's Jira target configuration.")]
    public static object Config() => new
    {
        baseUrl = Guard.BaseUrl,
        apiVersion = Guard.ApiVersion,
        hasBearerToken = !string.IsNullOrWhiteSpace(Guard.BearerToken),
        hasBasicAuth = !string.IsNullOrWhiteSpace(Guard.Email) && !string.IsNullOrWhiteSpace(Guard.ApiToken)
    };

    [McpServerTool(ReadOnly = true)]
    [Description("Search Jira issues with JQL.")]
    public static Task<ApiResult> SearchIssues(string jql, int startAt = 0, int maxResults = 100, string[]? fields = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["jql"] = jql,
            ["startAt"] = Math.Max(startAt, 0),
            ["maxResults"] = Math.Clamp(maxResults, 1, 100),
            ["fields"] = fields is { Length: > 0 } ? fields : new[] { "summary", "status", "assignee", "issuetype", "priority", "updated" }
        };
        return Send(HttpMethod.Post, $"/rest/api/{Guard.ApiVersion}/search", body: body);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get a Jira issue.")]
    public static Task<ApiResult> GetIssue(string issueKey, string[]? fields = null)
    {
        var query = new Dictionary<string, string>();
        if (fields is { Length: > 0 }) query["fields"] = string.Join(",", fields);
        return Send(HttpMethod.Get, $"/rest/api/{Guard.ApiVersion}/issue/" + Uri.EscapeDataString(issueKey), query);
    }

    [McpServerTool]
    [Description("Create a Jira issue.")]
    public static Task<ApiResult> CreateIssue(string projectKey, string issueType, string summary, string? descriptionText = null)
    {
        Guard.RequireWrites();
        var fields = new Dictionary<string, object?>
        {
            ["project"] = new Dictionary<string, object?> { ["key"] = projectKey },
            ["issuetype"] = new Dictionary<string, object?> { ["name"] = issueType },
            ["summary"] = summary
        };
        if (!string.IsNullOrWhiteSpace(descriptionText))
            fields["description"] = RichText(descriptionText);
        return Send(HttpMethod.Post, $"/rest/api/{Guard.ApiVersion}/issue", body: new Dictionary<string, object?> { ["fields"] = fields });
    }

    [McpServerTool]
    [Description("Update fields on an existing Jira issue. Only fields you pass are changed. descriptionText becomes an ADF doc that preserves paragraph and line breaks. labels replaces the whole label set.")]
    public static Task<ApiResult> UpdateIssue(string issueKey, string? summary = null, string? descriptionText = null, string[]? labels = null, string? assigneeAccountId = null, string? priority = null)
    {
        Guard.RequireWrites();
        var fields = new Dictionary<string, object?>();
        if (summary is not null) fields["summary"] = summary;
        if (descriptionText is not null) fields["description"] = RichText(descriptionText);
        if (labels is not null) fields["labels"] = labels;
        if (assigneeAccountId is not null)
            fields["assignee"] = string.IsNullOrWhiteSpace(assigneeAccountId)
                ? null
                : new Dictionary<string, object?> { ["accountId"] = assigneeAccountId };
        if (priority is not null)
            fields["priority"] = new Dictionary<string, object?> { ["name"] = priority };
        if (fields.Count == 0)
            return Task.FromResult(new ApiResult(0, false, "No fields to update. Provide at least one field."));
        return Send(HttpMethod.Put, $"/rest/api/{Guard.ApiVersion}/issue/" + Uri.EscapeDataString(issueKey),
            body: new Dictionary<string, object?> { ["fields"] = fields });
    }

    [McpServerTool]
    [Description("Add a comment to a Jira issue.")]
    public static Task<ApiResult> AddComment(string issueKey, string commentText)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Post, $"/rest/api/{Guard.ApiVersion}/issue/" + Uri.EscapeDataString(issueKey) + "/comment", body: new Dictionary<string, object?> { ["body"] = RichText(commentText) });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List available transitions for a Jira issue.")]
    public static Task<ApiResult> ListTransitions(string issueKey) =>
        Send(HttpMethod.Get, $"/rest/api/{Guard.ApiVersion}/issue/" + Uri.EscapeDataString(issueKey) + "/transitions");

    [McpServerTool]
    [Description("Transition a Jira issue.")]
    public static Task<ApiResult> TransitionIssue(string issueKey, string transitionId)
    {
        Guard.RequireWrites();
        var body = new Dictionary<string, object?> { ["transition"] = new Dictionary<string, object?> { ["id"] = transitionId } };
        return Send(HttpMethod.Post, $"/rest/api/{Guard.ApiVersion}/issue/" + Uri.EscapeDataString(issueKey) + "/transitions", body: body);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List Jira projects.")]
    public static Task<ApiResult> ListProjects() => Send(HttpMethod.Get, $"/rest/api/{Guard.ApiVersion}/project/search");

    private static async Task<ApiResult> Send(HttpMethod method, string path, Dictionary<string, string>? query = null, object? body = null)
    {
        Uri? uri = null;
        try
        {
            uri = BuildUri(path, query);
            using var request = new HttpRequestMessage(method, uri);
            ApplyAuth(request);
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
            return new ApiResult(0, false, $"Jira request failed for {uri?.ToString() ?? Guard.BaseUrl}: {ex.Message}");
        }
    }

    private static Uri BuildUri(string path, Dictionary<string, string>? query)
    {
        var builder = new UriBuilder(new Uri(new Uri(Guard.BaseUrl), path));
        if (query is { Count: > 0 })
            builder.Query = string.Join("&", query.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return builder.Uri;
    }

    private static void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Guard.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Guard.BearerToken);
            return;
        }
        if (!string.IsNullOrWhiteSpace(Guard.Email) && !string.IsNullOrWhiteSpace(Guard.ApiToken))
        {
            var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guard.Email + ":" + Guard.ApiToken));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
        }
    }

    private static object RichText(string text) =>
        Guard.ApiVersion == "2" ? text : AtlassianDoc(text);

    private static Dictionary<string, object?> AtlassianDoc(string text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.None);
        var content = paragraphs.Select(ParagraphNode).ToArray<object>();
        return new()
        {
            ["type"] = "doc",
            ["version"] = 1,
            ["content"] = content
        };
    }

    private static Dictionary<string, object?> ParagraphNode(string paragraph)
    {
        var lines = paragraph.Split('\n');
        var inline = new List<object>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                inline.Add(new Dictionary<string, object?> { ["type"] = "hardBreak" });
            if (lines[i].Length > 0)
                inline.Add(new Dictionary<string, object?> { ["type"] = "text", ["text"] = lines[i] });
        }
        var node = new Dictionary<string, object?> { ["type"] = "paragraph" };
        if (inline.Count > 0)
            node["content"] = inline.ToArray();
        return node;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal static class Guard
{
    public static string BaseUrl => (Environment.GetEnvironmentVariable("JIRA_BASE_URL") ?? "http://jira.local").TrimEnd('/') + "/";
    public static string ApiVersion
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("JIRA_API_VERSION");
            var value = string.IsNullOrWhiteSpace(configured) ? "3" : configured.Trim();
            return value == "2" ? "2" : "3";
        }
    }
    public static string? Email => Environment.GetEnvironmentVariable("JIRA_EMAIL");
    public static string? ApiToken => Environment.GetEnvironmentVariable("JIRA_API_TOKEN");
    public static string? BearerToken => Environment.GetEnvironmentVariable("JIRA_BEARER_TOKEN");

    public static void RequireWrites()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_JIRA_WRITES"), "false", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Jira write tools are disabled because MCP_ENABLE_JIRA_WRITES=false.");
    }
}

public sealed record ApiResult(int StatusCode, bool Success, string Body);
