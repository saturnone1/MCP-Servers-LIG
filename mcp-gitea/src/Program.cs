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
    .WithTools<GiteaTools>();
#pragma warning restore MCP9004

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", server = "mcp-gitea", gitea = Guard.BaseUrl }));
app.MapMcp("/mcp");
app.MapMcp("");
app.Run();

public sealed class GiteaTools
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromHours(1) };

    [McpServerTool(ReadOnly = true)]
    [Description("Return this MCP server Gitea target configuration.")]
    public static object Config() => new { baseUrl = Guard.BaseUrl, hasToken = !string.IsNullOrWhiteSpace(Guard.Token), writesEnabled = Guard.WritesEnabled };

    [McpServerTool(ReadOnly = true)]
    [Description("Get the Gitea version of the configured instance.")]
    public static Task<ApiResult> GetVersion() => Send(HttpMethod.Get, "/api/v1/version");

    [McpServerTool(ReadOnly = true)]
    [Description("Get the user that owns the configured Gitea token.")]
    public static Task<ApiResult> GetMe() => Send(HttpMethod.Get, "/api/v1/user");

    [McpServerTool(ReadOnly = true)]
    [Description("List organizations the configured Gitea user belongs to.")]
    public static Task<ApiResult> ListMyOrgs(int page = 1, int limit = 50) =>
        Send(HttpMethod.Get, "/api/v1/user/orgs", Paging(page, limit));

    [McpServerTool(ReadOnly = true)]
    [Description("Search Gitea users by keyword.")]
    public static Task<ApiResult> SearchUsers(string query, int page = 1, int limit = 50)
    {
        var parameters = Paging(page, limit);
        parameters["q"] = query;
        return Send(HttpMethod.Get, "/api/v1/users/search", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Search Gitea repositories by keyword. Optionally restrict to one owner.")]
    public static Task<ApiResult> SearchRepos(string? query = null, string? owner = null, int page = 1, int limit = 50)
    {
        var parameters = Paging(page, limit);
        if (!string.IsNullOrWhiteSpace(query)) parameters["q"] = query;
        if (!string.IsNullOrWhiteSpace(owner)) parameters["owner"] = owner;
        return Send(HttpMethod.Get, "/api/v1/repos/search", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List repositories owned by the configured Gitea token.")]
    public static Task<ApiResult> ListMyRepos(int page = 1, int limit = 50) =>
        Send(HttpMethod.Get, "/api/v1/user/repos", Paging(page, limit));

    [McpServerTool(ReadOnly = true)]
    [Description("List repositories that belong to a Gitea organization.")]
    public static Task<ApiResult> ListOrgRepos(string org, int page = 1, int limit = 50) =>
        Send(HttpMethod.Get, $"/api/v1/orgs/{Encode(org)}/repos", Paging(page, limit));

    [McpServerTool(ReadOnly = true)]
    [Description("Get one Gitea repository.")]
    public static Task<ApiResult> GetRepo(string owner, string repo) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}");

    [McpServerTool]
    [Description("Create a Gitea repository for the token owner, or for an organization when org is supplied.")]
    public static Task<ApiResult> CreateRepo(string name, string? org = null, string? description = null, bool isPrivate = true, bool autoInit = false, string? defaultBranch = null)
    {
        Guard.RequireWrites();
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["private"] = isPrivate,
            ["auto_init"] = autoInit
        };
        if (!string.IsNullOrWhiteSpace(description)) body["description"] = description;
        if (!string.IsNullOrWhiteSpace(defaultBranch)) body["default_branch"] = defaultBranch;
        var path = string.IsNullOrWhiteSpace(org) ? "/api/v1/user/repos" : $"/api/v1/orgs/{Encode(org)}/repos";
        return Send(HttpMethod.Post, path, body: body);
    }

    [McpServerTool]
    [Description("Fork a Gitea repository into the token owner or into an organization.")]
    public static Task<ApiResult> ForkRepo(string owner, string repo, string? organization = null)
    {
        Guard.RequireWrites();
        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(organization)) body["organization"] = organization;
        return Send(HttpMethod.Post, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/forks", body: body);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List branches of a Gitea repository.")]
    public static Task<ApiResult> ListBranches(string owner, string repo, int page = 1, int limit = 50) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/branches", Paging(page, limit));

    [McpServerTool]
    [Description("Create a branch in a Gitea repository from an existing branch.")]
    public static Task<ApiResult> CreateBranch(string owner, string repo, string newBranchName, string? oldBranchName = null)
    {
        Guard.RequireWrites();
        var body = new Dictionary<string, object?> { ["new_branch_name"] = newBranchName };
        if (!string.IsNullOrWhiteSpace(oldBranchName)) body["old_branch_name"] = oldBranchName;
        return Send(HttpMethod.Post, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/branches", body: body);
    }

    [McpServerTool]
    [Description("Delete a branch from a Gitea repository.")]
    public static Task<ApiResult> DeleteBranch(string owner, string repo, string branch)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Delete, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/branches/{Encode(branch)}");
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List tags of a Gitea repository.")]
    public static Task<ApiResult> ListTags(string owner, string repo, int page = 1, int limit = 50) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/tags", Paging(page, limit));

    [McpServerTool(ReadOnly = true)]
    [Description("List commits of a Gitea repository, optionally for one branch or path.")]
    public static Task<ApiResult> ListCommits(string owner, string repo, string? sha = null, string? path = null, int page = 1, int limit = 50)
    {
        var parameters = Paging(page, limit);
        if (!string.IsNullOrWhiteSpace(sha)) parameters["sha"] = sha;
        if (!string.IsNullOrWhiteSpace(path)) parameters["path"] = path;
        return Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/commits", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get one commit of a Gitea repository by SHA or ref.")]
    public static Task<ApiResult> GetCommit(string owner, string repo, string reference) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/git/commits/{Encode(reference)}");

    [McpServerTool(ReadOnly = true)]
    [Description("Get the file tree of a Gitea repository for one ref.")]
    public static Task<ApiResult> GetRepositoryTree(string owner, string repo, string reference = "main", bool recursive = true, int page = 1, int limit = 100)
    {
        var parameters = Paging(page, limit);
        parameters["recursive"] = recursive ? "true" : "false";
        return Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/git/trees/{Encode(reference)}", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List the entries of a directory in a Gitea repository.")]
    public static Task<ApiResult> GetDirContents(string owner, string repo, string directoryPath = "", string reference = "main") =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/contents/{EncodePath(directoryPath)}", new Dictionary<string, string> { ["ref"] = reference });

    [McpServerTool(ReadOnly = true)]
    [Description("Read a repository file from Gitea. Returns decoded text by default, or the raw base64 metadata payload when decode is false.")]
    public static async Task<ApiResult> GetFileContents(string owner, string repo, string filePath, string reference = "main", bool decode = true)
    {
        var metadata = await FetchFile(owner, repo, filePath, reference);
        if (!decode || !metadata.Success) return metadata;
        var content = ReadContent(metadata.Body);
        return content is null ? metadata : metadata with { Body = content };
    }

    [McpServerTool]
    [Description("Create or update a repository file in Gitea. Existing files are updated with their current blob sha.")]
    public static async Task<ApiResult> CreateOrUpdateFile(string owner, string repo, string filePath, string branch, string content, string commitMessage)
    {
        Guard.RequireWrites();
        var existing = await FetchFile(owner, repo, filePath, branch);
        var sha = existing.Success ? ReadSha(existing.Body) : null;
        return await PutFileContent(owner, repo, filePath, branch, content, commitMessage, sha);
    }

    [McpServerTool]
    [Description("Append content to the end of an existing Gitea repository file without regenerating the whole body.")]
    public static Task<ApiResult> AppendToFile(string owner, string repo, string filePath, string branch, string content, string commitMessage) =>
        EditFile(owner, repo, filePath, branch, commitMessage, existing => existing + content);

    [McpServerTool]
    [Description("Prepend content to the beginning of an existing Gitea repository file.")]
    public static Task<ApiResult> PrependToFile(string owner, string repo, string filePath, string branch, string content, string commitMessage) =>
        EditFile(owner, repo, filePath, branch, commitMessage, existing => content + existing);

    [McpServerTool]
    [Description("Replace the first occurrence of a literal fragment inside an existing Gitea repository file.")]
    public static Task<ApiResult> ReplaceInFile(string owner, string repo, string filePath, string branch, string search, string replacement, string commitMessage) =>
        EditFile(owner, repo, filePath, branch, commitMessage, existing =>
        {
            var index = existing.IndexOf(search, StringComparison.Ordinal);
            if (index < 0)
                throw new InvalidOperationException($"Fragment not found in {filePath}: {search}");
            return string.Concat(existing.AsSpan(0, index), replacement, existing.AsSpan(index + search.Length));
        });

    [McpServerTool]
    [Description("Delete a repository file from Gitea.")]
    public static async Task<ApiResult> DeleteFile(string owner, string repo, string filePath, string branch, string commitMessage)
    {
        Guard.RequireWrites();
        var existing = await FetchFile(owner, repo, filePath, branch);
        if (!existing.Success) return existing;
        var sha = ReadSha(existing.Body);
        if (sha is null) return existing with { Success = false, Body = $"Gitea did not return a blob sha for {filePath}." };
        return await Send(HttpMethod.Delete, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/contents/{EncodePath(filePath)}",
            body: new Dictionary<string, object?> { ["branch"] = branch, ["message"] = commitMessage, ["sha"] = sha });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List issues of a Gitea repository.")]
    public static Task<ApiResult> ListIssues(string owner, string repo, string state = "open", string? labels = null, int page = 1, int limit = 50)
    {
        var parameters = Paging(page, limit);
        parameters["state"] = state;
        parameters["type"] = "issues";
        if (!string.IsNullOrWhiteSpace(labels)) parameters["labels"] = labels;
        return Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/issues", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Search issues across the Gitea instance.")]
    public static Task<ApiResult> SearchIssues(string query, string state = "open", int page = 1, int limit = 50)
    {
        var parameters = Paging(page, limit);
        parameters["q"] = query;
        parameters["state"] = state;
        parameters["type"] = "issues";
        return Send(HttpMethod.Get, "/api/v1/repos/issues/search", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get one issue of a Gitea repository by index.")]
    public static Task<ApiResult> GetIssue(string owner, string repo, int index) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/issues/{index}");

    [McpServerTool(ReadOnly = true)]
    [Description("List comments of one Gitea issue or pull request.")]
    public static Task<ApiResult> ListIssueComments(string owner, string repo, int index, int page = 1, int limit = 50) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/issues/{index}/comments", Paging(page, limit));

    [McpServerTool]
    [Description("Create an issue in a Gitea repository.")]
    public static Task<ApiResult> CreateIssue(string owner, string repo, string title, string? body = null, string[]? assignees = null, long? milestone = null)
    {
        Guard.RequireWrites();
        var payload = new Dictionary<string, object?> { ["title"] = title };
        if (!string.IsNullOrWhiteSpace(body)) payload["body"] = body;
        if (assignees is { Length: > 0 }) payload["assignees"] = assignees;
        if (milestone is not null) payload["milestone"] = milestone;
        return Send(HttpMethod.Post, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/issues", body: payload);
    }

    [McpServerTool]
    [Description("Edit the title, body, or state of a Gitea issue. State accepts open or closed.")]
    public static Task<ApiResult> EditIssue(string owner, string repo, int index, string? title = null, string? body = null, string? state = null)
    {
        Guard.RequireWrites();
        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(title)) payload["title"] = title;
        if (body is not null) payload["body"] = body;
        if (!string.IsNullOrWhiteSpace(state)) payload["state"] = state;
        return Send(HttpMethod.Patch, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/issues/{index}", body: payload);
    }

    [McpServerTool]
    [Description("Add a comment to a Gitea issue or pull request.")]
    public static Task<ApiResult> CreateIssueComment(string owner, string repo, int index, string body)
    {
        Guard.RequireWrites();
        return Send(HttpMethod.Post, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/issues/{index}/comments", body: new Dictionary<string, object?> { ["body"] = body });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List pull requests of a Gitea repository.")]
    public static Task<ApiResult> ListPullRequests(string owner, string repo, string state = "open", int page = 1, int limit = 50)
    {
        var parameters = Paging(page, limit);
        parameters["state"] = state;
        return Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/pulls", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Get one pull request of a Gitea repository by index.")]
    public static Task<ApiResult> GetPullRequest(string owner, string repo, int index) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/pulls/{index}");

    [McpServerTool(ReadOnly = true)]
    [Description("Get the diff or patch of one Gitea pull request. Format accepts diff or patch.")]
    public static Task<ApiResult> GetPullRequestDiff(string owner, string repo, int index, string format = "diff")
    {
        var normalized = string.Equals(format, "patch", StringComparison.OrdinalIgnoreCase) ? "patch" : "diff";
        return Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/pulls/{index}.{normalized}");
    }

    [McpServerTool]
    [Description("Create a pull request in a Gitea repository. targetBranch is the branch that receives the change.")]
    public static Task<ApiResult> CreatePullRequest(string owner, string repo, string head, string targetBranch, string title, string? body = null)
    {
        Guard.RequireWrites();
        var payload = new Dictionary<string, object?>
        {
            ["head"] = head,
            ["base"] = targetBranch,
            ["title"] = title
        };
        if (!string.IsNullOrWhiteSpace(body)) payload["body"] = body;
        return Send(HttpMethod.Post, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/pulls", body: payload);
    }

    [McpServerTool]
    [Description("Merge a Gitea pull request. Style accepts merge, rebase, rebase-merge, squash, or manually-merged.")]
    public static Task<ApiResult> MergePullRequest(string owner, string repo, int index, string style = "merge", string? title = null, string? message = null)
    {
        Guard.RequireWrites();
        var payload = new Dictionary<string, object?> { ["Do"] = style };
        if (!string.IsNullOrWhiteSpace(title)) payload["MergeTitleField"] = title;
        if (!string.IsNullOrWhiteSpace(message)) payload["MergeMessageField"] = message;
        return Send(HttpMethod.Post, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/pulls/{index}/merge", body: payload);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List releases of a Gitea repository.")]
    public static Task<ApiResult> ListReleases(string owner, string repo, int page = 1, int limit = 50) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/releases", Paging(page, limit));

    [McpServerTool(ReadOnly = true)]
    [Description("Get the latest release of a Gitea repository.")]
    public static Task<ApiResult> GetLatestRelease(string owner, string repo) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/releases/latest");

    [McpServerTool]
    [Description("Create a release in a Gitea repository.")]
    public static Task<ApiResult> CreateRelease(string owner, string repo, string tagName, string? targetCommitish = null, string? name = null, string? body = null, bool draft = false, bool prerelease = false)
    {
        Guard.RequireWrites();
        var payload = new Dictionary<string, object?>
        {
            ["tag_name"] = tagName,
            ["draft"] = draft,
            ["prerelease"] = prerelease
        };
        if (!string.IsNullOrWhiteSpace(targetCommitish)) payload["target_commitish"] = targetCommitish;
        if (!string.IsNullOrWhiteSpace(name)) payload["name"] = name;
        if (!string.IsNullOrWhiteSpace(body)) payload["body"] = body;
        return Send(HttpMethod.Post, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/releases", body: payload);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List labels of a Gitea repository.")]
    public static Task<ApiResult> ListLabels(string owner, string repo, int page = 1, int limit = 50) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/labels", Paging(page, limit));

    [McpServerTool(ReadOnly = true)]
    [Description("List milestones of a Gitea repository.")]
    public static Task<ApiResult> ListMilestones(string owner, string repo, string state = "open", int page = 1, int limit = 50)
    {
        var parameters = Paging(page, limit);
        parameters["state"] = state;
        return Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/milestones", parameters);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Read a Gitea repository wiki page. Omit pageName to list the wiki pages.")]
    public static Task<ApiResult> WikiRead(string owner, string repo, string? pageName = null, int page = 1, int limit = 50) =>
        string.IsNullOrWhiteSpace(pageName)
            ? Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/wiki/pages", Paging(page, limit))
            : Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/wiki/page/{Encode(pageName)}");

    [McpServerTool(ReadOnly = true)]
    [Description("List Gitea Actions workflow runs of a repository.")]
    public static Task<ApiResult> ListActionRuns(string owner, string repo, int page = 1, int limit = 50) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/actions/runs", Paging(page, limit));

    [McpServerTool(ReadOnly = true)]
    [Description("List notifications for the configured Gitea user.")]
    public static Task<ApiResult> ListNotifications(bool all = false, int page = 1, int limit = 50)
    {
        var parameters = Paging(page, limit);
        parameters["all"] = all ? "true" : "false";
        return Send(HttpMethod.Get, "/api/v1/notifications", parameters);
    }

    private static async Task<ApiResult> EditFile(string owner, string repo, string filePath, string branch, string commitMessage, Func<string, string> transform)
    {
        Guard.RequireWrites();
        var existing = await FetchFile(owner, repo, filePath, branch);
        if (!existing.Success) return existing;
        var content = ReadContent(existing.Body);
        var sha = ReadSha(existing.Body);
        if (content is null || sha is null)
            return existing with { Success = false, Body = $"Gitea did not return decodable content and a blob sha for {filePath}." };
        return await PutFileContent(owner, repo, filePath, branch, transform(content), commitMessage, sha);
    }

    private static Task<ApiResult> FetchFile(string owner, string repo, string filePath, string reference) =>
        Send(HttpMethod.Get, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/contents/{EncodePath(filePath)}", new Dictionary<string, string> { ["ref"] = reference });

    private static Task<ApiResult> PutFileContent(string owner, string repo, string filePath, string branch, string content, string commitMessage, string? sha)
    {
        var body = new Dictionary<string, object?>
        {
            ["branch"] = branch,
            ["message"] = commitMessage,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
        };
        var method = HttpMethod.Post;
        if (!string.IsNullOrWhiteSpace(sha))
        {
            body["sha"] = sha;
            method = HttpMethod.Put;
        }
        return Send(method, $"/api/v1/repos/{Encode(owner)}/{Encode(repo)}/contents/{EncodePath(filePath)}", body: body);
    }

    private static string? ReadContent(string payload)
    {
        var encoded = ReadProperty(payload, "content");
        if (encoded is null) return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? ReadSha(string payload) => ReadProperty(payload, "sha");

    private static string? ReadProperty(string payload, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> Paging(int page, int limit) => new()
    {
        ["page"] = Math.Max(page, 1).ToString(),
        ["limit"] = Math.Clamp(limit, 1, 100).ToString()
    };

    private static async Task<ApiResult> Send(HttpMethod method, string path, Dictionary<string, string>? query = null, object? body = null)
    {
        Uri? uri = null;
        try
        {
            uri = BuildUri(path, query);
            using var request = new HttpRequestMessage(method, uri);
            if (!string.IsNullOrWhiteSpace(Guard.Token))
                request.Headers.TryAddWithoutValidation("Authorization", "token " + Guard.Token);
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
            return new ApiResult(0, false, $"Gitea request failed for {uri?.ToString() ?? Guard.BaseUrl}: {ex.Message}");
        }
    }

    private static Uri BuildUri(string path, Dictionary<string, string>? query)
    {
        var builder = new UriBuilder(new Uri(new Uri(Guard.BaseUrl), path));
        if (query is { Count: > 0 })
            builder.Query = string.Join("&", query.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return builder.Uri;
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    // Repository file paths keep their separators; only the individual segments are escaped.
    private static string EncodePath(string value) => string.Join('/', value.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal static class Guard
{
    public static string BaseUrl => (Environment.GetEnvironmentVariable("GITEA_BASE_URL") ?? "http://gitea.local").TrimEnd('/') + "/";
    public static string? Token => Environment.GetEnvironmentVariable("GITEA_TOKEN");
    public static bool WritesEnabled => !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_GITEA_WRITES"), "false", StringComparison.OrdinalIgnoreCase);

    public static void RequireWrites()
    {
        if (!WritesEnabled)
            throw new UnauthorizedAccessException("Gitea write tools are disabled because MCP_ENABLE_GITEA_WRITES=false.");
    }
}

public sealed record ApiResult(int StatusCode, bool Success, string Body);
