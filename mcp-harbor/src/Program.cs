using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080");
builder.Services.AddHttpClient();
#pragma warning disable MCP9004
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.EnableLegacySse = true)
    .WithTools<HarborTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-harbor", harbor = Guard.BaseUrl }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class HarborTools
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromHours(1) };

    [McpServerTool(ReadOnly = true)]
    [Description("Return this MCP server Harbor target configuration.")]
    public static object Config() => new
    {
        baseUrl = Guard.BaseUrl,
        apiRoot = Guard.ApiRoot,
        user = Guard.Username,
        hasCredentials = !string.IsNullOrWhiteSpace(Guard.Username) && !string.IsNullOrWhiteSpace(Guard.Password),
        writesEnabled = Guard.WritesEnabled
    };

    [McpServerTool(ReadOnly = true)]
    [Description("Get the health of the Harbor instance and each of its components.")]
    public static Task<ApiResult> GetHealth() => Send(HttpMethod.Get, "health");

    [McpServerTool(ReadOnly = true)]
    [Description("Get the Harbor version and system information.")]
    public static Task<ApiResult> GetSystemInfo() => Send(HttpMethod.Get, "systeminfo");

    [McpServerTool(ReadOnly = true)]
    [Description("Get Harbor project, repository, and storage statistics for the current user.")]
    public static Task<ApiResult> GetStatistics() => Send(HttpMethod.Get, "statistics");

    [McpServerTool(ReadOnly = true)]
    [Description("Get total and free storage of the Harbor system volume.")]
    public static Task<ApiResult> GetVolumes() => Send(HttpMethod.Get, "systeminfo/volumes");

    [McpServerTool(ReadOnly = true)]
    [Description("Search Harbor projects and repositories by keyword.")]
    public static Task<ApiResult> Search(string query) =>
        Send(HttpMethod.Get, "search", new Dictionary<string, string> { ["q"] = query });

    [McpServerTool(ReadOnly = true)]
    [Description("List Harbor projects. Set visibility to public or private to filter, or leave it empty for all.")]
    public static Task<ApiResult> ListProjects(string? name = null, string? owner = null, string? visibility = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        if (!string.IsNullOrWhiteSpace(name)) parameters["name"] = name;
        if (!string.IsNullOrWhiteSpace(owner)) parameters["owner"] = owner;
        if (string.Equals(visibility, "public", StringComparison.OrdinalIgnoreCase)) parameters["public"] = "true";
        if (string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase)) parameters["public"] = "false";
        return Send(HttpMethod.Get, "projects", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get one Harbor project by name or numeric id.")]
    public static Task<ApiResult> GetProject(string project) =>
        Send(HttpMethod.Get, $"projects/{Encode(project)}");

    [McpServerTool(ReadOnly = true)]
    [Description("Get the repository, chart, and quota summary of a Harbor project.")]
    public static Task<ApiResult> GetProjectSummary(string project) =>
        Send(HttpMethod.Get, $"projects/{Encode(project)}/summary");

    [McpServerTool(ReadOnly = true)]
    [Description("List the members of a Harbor project.")]
    public static Task<ApiResult> ListProjectMembers(string project, string? entityName = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        if (!string.IsNullOrWhiteSpace(entityName)) parameters["entityname"] = entityName;
        return Send(HttpMethod.Get, $"projects/{Encode(project)}/members", parameters);
    }

    [McpServerTool]
    [Description("Create a Harbor project. storageLimitBytes of -1 means unlimited.")]
    public static Task<ApiResult> CreateProject(string projectName, bool isPublic = false, long storageLimitBytes = -1, long? registryId = null)
    {
        Guard.RequireWrites();
        var body = new Dictionary<string, object?>
        {
            ["project_name"] = projectName,
            ["storage_limit"] = storageLimitBytes,
            ["metadata"] = new Dictionary<string, object?> { ["public"] = isPublic ? "true" : "false" }
        };
        if (registryId is not null) body["registry_id"] = registryId;
        return Send(HttpMethod.Post, "projects", body: body);
    }

    [McpServerTool]
    [Description("Delete a Harbor project. The project must not contain repositories.")]
    public static Task<ApiResult> DeleteProject(string project)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Delete, $"projects/{Encode(project)}");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the repositories of a Harbor project.")]
    public static Task<ApiResult> ListRepositories(string project, string? query = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        if (!string.IsNullOrWhiteSpace(query)) parameters["q"] = query;
        return Send(HttpMethod.Get, $"projects/{Encode(project)}/repositories", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get one repository of a Harbor project. Use the repository name without the project prefix.")]
    public static Task<ApiResult> GetRepository(string project, string repository) =>
        Send(HttpMethod.Get, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}");

    [McpServerTool]
    [Description("Delete a repository and every artifact it holds from a Harbor project.")]
    public static Task<ApiResult> DeleteRepository(string project, string repository)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Delete, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the artifacts of a Harbor repository. Harbor v2 stores container images and Helm charts alike as artifacts.")]
    public static Task<ApiResult> ListArtifacts(string project, string repository, bool withTag = true, bool withScanOverview = false, bool withLabel = false, string? query = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        parameters["with_tag"] = withTag ? "true" : "false";
        parameters["with_scan_overview"] = withScanOverview ? "true" : "false";
        parameters["with_label"] = withLabel ? "true" : "false";
        if (!string.IsNullOrWhiteSpace(query)) parameters["q"] = query;
        return Send(HttpMethod.Get, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}/artifacts", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get one Harbor artifact by tag name or digest.")]
    public static Task<ApiResult> GetArtifact(string project, string repository, string reference, bool withScanOverview = true, bool withTag = true)
    {
        var parameters = new Dictionary<string, string>
        {
            ["with_scan_overview"] = withScanOverview ? "true" : "false",
            ["with_tag"] = withTag ? "true" : "false"
        };
        return Send(HttpMethod.Get, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}/artifacts/{Encode(reference)}", parameters);
    }

    [McpServerTool]
    [Description("Delete one Harbor artifact by tag name or digest.")]
    public static Task<ApiResult> DeleteArtifact(string project, string repository, string reference)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Delete, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}/artifacts/{Encode(reference)}");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the tags attached to one Harbor artifact.")]
    public static Task<ApiResult> ListArtifactTags(string project, string repository, string reference, int page = 1, int pageSize = 50) =>
        Send(HttpMethod.Get, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}/artifacts/{Encode(reference)}/tags", Paging(page, pageSize));

    [McpServerTool]
    [Description("Attach a tag to one Harbor artifact.")]
    public static Task<ApiResult> CreateTag(string project, string repository, string reference, string tag)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Post, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}/artifacts/{Encode(reference)}/tags",
            body: new Dictionary<string, object?> { ["name"] = tag });
    }

    [McpServerTool]
    [Description("Delete one tag from a Harbor artifact. The artifact itself stays until its last tag is removed.")]
    public static Task<ApiResult> DeleteTag(string project, string repository, string reference, string tag)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Delete, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}/artifacts/{Encode(reference)}/tags/{Encode(tag)}");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get the vulnerability report of one Harbor artifact. The artifact must have been scanned already.")]
    public static Task<ApiResult> GetVulnerabilities(string project, string repository, string reference) =>
        Send(HttpMethod.Get, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}/artifacts/{Encode(reference)}/additions/vulnerabilities");

    [McpServerTool(ReadOnly = true)]
    [Description("Get the build history of one Harbor artifact.")]
    public static Task<ApiResult> GetBuildHistory(string project, string repository, string reference) =>
        Send(HttpMethod.Get, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}/artifacts/{Encode(reference)}/additions/build_history");

    [McpServerTool]
    [Description("Start a vulnerability scan of one Harbor artifact.")]
    public static Task<ApiResult> ScanArtifact(string project, string repository, string reference)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Post, $"projects/{Encode(project)}/repositories/{EncodeRepository(repository)}/artifacts/{Encode(reference)}/scan");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the vulnerability scanner registrations of the Harbor instance.")]
    public static Task<ApiResult> ListScanners(int page = 1, int pageSize = 50) =>
        Send(HttpMethod.Get, "scanners", Paging(page, pageSize));

    [McpServerTool(ReadOnly = true)]
    [Description("List Harbor storage quotas per project.")]
    public static Task<ApiResult> ListQuotas(string? reference = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        if (!string.IsNullOrWhiteSpace(reference)) parameters["reference"] = reference;
        return Send(HttpMethod.Get, "quotas", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List Harbor labels. Scope accepts g for global or p for project labels.")]
    public static Task<ApiResult> ListLabels(string scope = "g", long? projectId = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        parameters["scope"] = scope;
        if (projectId is not null) parameters["project_id"] = projectId.Value.ToString();
        return Send(HttpMethod.Get, "labels", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the audit log entries of the Harbor instance.")]
    public static Task<ApiResult> ListAuditLogs(string? query = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        if (!string.IsNullOrWhiteSpace(query)) parameters["q"] = query;
        return Send(HttpMethod.Get, "audit-logs", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the remote registry endpoints configured in Harbor.")]
    public static Task<ApiResult> ListRegistries(string? query = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        if (!string.IsNullOrWhiteSpace(query)) parameters["q"] = query;
        return Send(HttpMethod.Get, "registries", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the Harbor replication policies.")]
    public static Task<ApiResult> ListReplicationPolicies(string? query = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        if (!string.IsNullOrWhiteSpace(query)) parameters["q"] = query;
        return Send(HttpMethod.Get, "replication/policies", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the Harbor replication executions, optionally for one policy.")]
    public static Task<ApiResult> ListReplicationExecutions(long? policyId = null, int page = 1, int pageSize = 50)
    {
        var parameters = Paging(page, pageSize);
        if (policyId is not null) parameters["policy_id"] = policyId.Value.ToString();
        return Send(HttpMethod.Get, "replication/executions", parameters);
    }

    [McpServerTool]
    [Description("Start a Harbor replication execution for one policy.")]
    public static Task<ApiResult> StartReplication(long policyId)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Post, "replication/executions", body: new Dictionary<string, object?> { ["policy_id"] = policyId });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the webhook policies of a Harbor project.")]
    public static Task<ApiResult> ListWebhookPolicies(string project, int page = 1, int pageSize = 50) =>
        Send(HttpMethod.Get, $"projects/{Encode(project)}/webhook/policies", Paging(page, pageSize));

    [McpServerTool(ReadOnly = true)]
    [Description("Get the Harbor system configuration. Requires an administrator account.")]
    public static Task<ApiResult> GetConfigurations() => Send(HttpMethod.Get, "configurations");

    [McpServerTool]
    [Description("Update the Harbor system configuration from a JSON object. Requires an administrator account and confirm=true, because a bad value can lock every user out of the instance.")]
    public static Task<ApiResult> UpdateConfigurations(string settingsJson, bool confirm = false)
    {
        Guard.RequireWrites();
        if (!confirm)
            throw new InvalidOperationException("Refusing to update Harbor configurations without confirm=true. Review the payload with get_configurations first.");

        JsonElement settings;
        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("settingsJson must be a JSON object of Harbor configuration keys.");
            settings = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"settingsJson is not valid JSON: {ex.Message}", ex);
        }

        return Send(HttpMethod.Put, "configurations", body: settings);
    }

    private static Dictionary<string, string> Paging(int page, int pageSize) => new()
    {
        ["page"] = Math.Max(page, 1).ToString(),
        ["page_size"] = Math.Clamp(pageSize, 1, 100).ToString()
    };

    private static async Task<ApiResult> Send(HttpMethod method, string path, Dictionary<string, string>? query = null, object? body = null)
    {
        Uri? uri = null;
        try
        {
            uri = BuildUri(path, query);
            using var request = new HttpRequestMessage(method, uri);
            if (Guard.AuthorizationHeader is { } authorization)
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
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
            return new ApiResult(0, false, $"Harbor request failed for {uri?.ToString() ?? Guard.BaseUrl}: {ex.Message}");
        }
    }

    private static Uri BuildUri(string path, Dictionary<string, string>? query)
    {
        var builder = new UriBuilder(new Uri(new Uri(Guard.ApiRoot), path));
        if (query is { Count: > 0 })
            builder.Query = string.Join("&", query.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return builder.Uri;
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    // Harbor addresses nested repositories such as team/service as a single path segment,
    // so the separators have to stay percent-encoded instead of becoming real path segments.
    private static string EncodeRepository(string value) => Uri.EscapeDataString(value.Trim('/'));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal static class Guard
{
    public static string BaseUrl => (Environment.GetEnvironmentVariable("HARBOR_BASE_URL") ?? "http://harbor.local").TrimEnd('/') + "/";
    public static string ApiRoot => BaseUrl + "api/v2.0/";
    public static string? Username => Environment.GetEnvironmentVariable("HARBOR_USERNAME");
    public static string? Password => Environment.GetEnvironmentVariable("HARBOR_PASSWORD");
    public static bool WritesEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_HARBOR_WRITES"), "false", StringComparison.OrdinalIgnoreCase);

    // Harbor accepts the same basic scheme for human accounts, CLI secrets, and robot accounts.
    public static string? AuthorizationHeader
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                return null;
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
            return "Basic " + credentials;
        }
    }

    public static void RequireWrites()
    {
        if (!WritesEnabled)
            throw new UnauthorizedAccessException("Harbor write tools are disabled because MCP_ENABLE_HARBOR_WRITES=false.");
    }
}

public sealed record ApiResult(int StatusCode, bool Success, string Body);
