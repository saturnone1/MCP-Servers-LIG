using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

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

    [McpServerTool]
    [Description("Update a Confluence page without managing version numbers manually. Fetches current version/space/title, submits with the next version, and retries once on 409 version conflict.")]
    public static Task<ApiResult> UpdatePageAuto(string id, string storageBody, string? title = null, string? parentId = null, bool minorEdit = false)
    {
        Guard.RequireWrites();
        return UpdateWithFetchedContext(id, _ => storageBody, title, parentId, minorEdit);
    }

    [McpServerTool]
    [Description("Append a storage-format fragment to the end of a Confluence page. The LLM sends only the new fragment; existing body and macros are preserved. Retries once on 409 version conflict.")]
    public static Task<ApiResult> AppendToPage(string id, string storageFragment, bool minorEdit = true)
    {
        Guard.RequireWrites();
        return UpdateWithFetchedContext(id, existing => existing + storageFragment, title: null, parentId: null, minorEdit);
    }

    [McpServerTool]
    [Description("Prepend a storage-format fragment to the beginning of a Confluence page. The LLM sends only the new fragment; existing body and macros are preserved. Retries once on 409 version conflict.")]
    public static Task<ApiResult> PrependToPage(string id, string storageFragment, bool minorEdit = true)
    {
        Guard.RequireWrites();
        return UpdateWithFetchedContext(id, existing => storageFragment + existing, title: null, parentId: null, minorEdit);
    }

    [McpServerTool]
    [Description("Replace one heading's section in a Confluence page. Section spans from the matched heading (exclusive) until the next heading of same or higher level. Fails on ambiguous heading matches unless occurrenceIndex is provided.")]
    public static Task<ApiResult> ReplaceSection(string id, string headingText, string newStorageFragment, int? occurrenceIndex = null, bool caseSensitive = false, bool minorEdit = true)
    {
        Guard.RequireWrites();
        return UpdateWithFetchedContext(id, existing =>
        {
            var section = LocateSection(existing, headingText, occurrenceIndex, caseSensitive);
            return existing[..section.ContentStart] + newStorageFragment + existing[section.ContentEnd..];
        }, title: null, parentId: null, minorEdit);
    }

    [McpServerTool]
    [Description("Append a storage-format fragment to the end of a specific section (right before the next heading of same or higher level). Fails on ambiguous heading matches unless occurrenceIndex is provided.")]
    public static Task<ApiResult> AppendToSection(string id, string headingText, string storageFragment, int? occurrenceIndex = null, bool caseSensitive = false, bool minorEdit = true)
    {
        Guard.RequireWrites();
        return UpdateWithFetchedContext(id, existing =>
        {
            var section = LocateSection(existing, headingText, occurrenceIndex, caseSensitive);
            return existing[..section.ContentEnd] + storageFragment + existing[section.ContentEnd..];
        }, title: null, parentId: null, minorEdit);
    }

    [McpServerTool]
    [Description("Insert a storage-format fragment immediately after a specific heading (at the top of its section). Fails on ambiguous heading matches unless occurrenceIndex is provided.")]
    public static Task<ApiResult> InsertAfterHeading(string id, string headingText, string storageFragment, int? occurrenceIndex = null, bool caseSensitive = false, bool minorEdit = true)
    {
        Guard.RequireWrites();
        return UpdateWithFetchedContext(id, existing =>
        {
            var section = LocateSection(existing, headingText, occurrenceIndex, caseSensitive);
            return existing[..section.ContentStart] + storageFragment + existing[section.ContentStart..];
        }, title: null, parentId: null, minorEdit);
    }

    [McpServerTool]
    [Description("Replace substring occurrences in a Confluence page body. Verifies actual count matches expectedOccurrences before writing, preventing unintended matches inside macro parameters. Set expectedOccurrences to null to skip verification.")]
    public static Task<ApiResult> FindReplaceText(string id, string find, string replace, int? expectedOccurrences = 1, bool minorEdit = true)
    {
        Guard.RequireWrites();
        if (string.IsNullOrEmpty(find))
            return Task.FromResult(new ApiResult(0, false, "find must be a non-empty string."));
        return UpdateWithFetchedContext(id, existing =>
        {
            var count = CountOccurrences(existing, find);
            if (count == 0)
                throw new InvalidOperationException($"'{find}' not found in page body.");
            if (expectedOccurrences.HasValue && count != expectedOccurrences.Value)
                throw new InvalidOperationException($"Expected {expectedOccurrences} occurrence(s) of '{find}' but found {count}. Aborting to avoid unintended matches.");
            return existing.Replace(find, replace);
        }, title: null, parentId: null, minorEdit);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Read one section from a Confluence page by heading text. Returns storage-format content between the matched heading and the next heading of same or higher level. Set includeHeading=true to include the heading tag itself.")]
    public static async Task<ApiResult> GetSection(string id, string headingText, int? occurrenceIndex = null, bool caseSensitive = false, bool includeHeading = false)
    {
        var fetch = await Send(HttpMethod.Get, "/rest/api/content/" + Uri.EscapeDataString(id),
            OptionalQuery(new Dictionary<string, string> { ["expand"] = "body.storage" }));
        if (!fetch.Success)
            return fetch;

        string body;
        try
        {
            body = ParsePageContext(fetch.Body).Body;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            var snippet = fetch.Body.Length > 500 ? fetch.Body[..500] + "..." : fetch.Body;
            return new ApiResult(0, false, $"Failed to parse page context for id {id}: {ex.Message}. Raw: {snippet}");
        }

        try
        {
            var section = LocateSection(body, headingText, occurrenceIndex, caseSensitive);
            var start = includeHeading ? section.HeadingStart : section.ContentStart;
            return new ApiResult(200, true, body[start..section.ContentEnd]);
        }
        catch (InvalidOperationException ex)
        {
            return new ApiResult(0, false, ex.Message);
        }
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Check whether a storage-format fragment is well-formed XHTML before committing it. Returns { valid, error?, headings, length }. Common HTML named entities are treated as valid.")]
    public static object PreviewStorage(string storageFragment)
    {
        var normalized = ReplaceNamedEntities(storageFragment ?? string.Empty);
        var wrapped = "<root xmlns:ac=\"http://atlassian.com/content\" xmlns:ri=\"http://atlassian.com/resource/identifier\">" + normalized + "</root>";
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(wrapped), settings);
            while (reader.Read()) { }
            return new
            {
                valid = true,
                length = (storageFragment ?? string.Empty).Length,
                headings = FindAllHeadings(storageFragment ?? string.Empty).Count
            };
        }
        catch (XmlException ex)
        {
            return new
            {
                valid = false,
                error = ex.Message,
                line = ex.LineNumber,
                column = ex.LinePosition
            };
        }
    }

    private static async Task<ApiResult> UpdateWithFetchedContext(string id, Func<string, string> transform, string? title, string? parentId, bool minorEdit)
    {
        var lastResult = new ApiResult(0, false, "no attempt executed");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var fetch = await Send(HttpMethod.Get, "/rest/api/content/" + Uri.EscapeDataString(id),
                OptionalQuery(new Dictionary<string, string> { ["expand"] = "version,space,body.storage" }));
            if (!fetch.Success)
                return fetch;

            int currentVersion;
            string spaceKey;
            string currentTitle;
            string existingBody;
            try
            {
                (currentVersion, spaceKey, currentTitle, existingBody) = ParsePageContext(fetch.Body);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
            {
                var snippet = fetch.Body.Length > 500 ? fetch.Body[..500] + "..." : fetch.Body;
                return new ApiResult(0, false, $"Failed to parse page context for id {id}: {ex.Message}. Raw: {snippet}");
            }

            string newBody;
            try
            {
                newBody = transform(existingBody);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResult(0, false, ex.Message);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return new ApiResult(0, false, ex.Message);
            }

            var effectiveTitle = string.IsNullOrWhiteSpace(title) ? currentTitle : title!;
            var body = PageBody("page", effectiveTitle, spaceKey, newBody, currentVersion + 1, parentId, minorEdit, id);
            lastResult = await Send(HttpMethod.Put, "/rest/api/content/" + Uri.EscapeDataString(id), body: body);
            if (lastResult.StatusCode != 409)
                return lastResult;
        }
        return lastResult;
    }

    private static readonly Regex HeadingRegex = new(@"<h([1-6])\b[^>]*>(.*?)</h\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagStripRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private sealed record Heading(int Level, string Text, int StartIndex, int EndIndex);
    private sealed record SectionRange(int HeadingStart, int ContentStart, int ContentEnd);

    private static List<Heading> FindAllHeadings(string body)
    {
        var results = new List<Heading>();
        foreach (Match m in HeadingRegex.Matches(body))
        {
            var level = int.Parse(m.Groups[1].Value);
            var innerHtml = m.Groups[2].Value;
            var plain = WebUtility.HtmlDecode(TagStripRegex.Replace(innerHtml, "")).Trim();
            results.Add(new Heading(level, plain, m.Index, m.Index + m.Length));
        }
        return results;
    }

    private static SectionRange LocateSection(string body, string headingText, int? occurrenceIndex, bool caseSensitive)
    {
        var headings = FindAllHeadings(body);
        if (headings.Count == 0)
            throw new InvalidOperationException("Page body has no <h1>..<h6> headings to target.");
        var needle = (headingText ?? string.Empty).Trim();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matches = headings.Where(h => string.Equals(h.Text, needle, comparison)).ToList();
        if (matches.Count == 0)
        {
            var preview = string.Join(" | ", headings.Take(10).Select(h => $"h{h.Level}:{h.Text}"));
            throw new InvalidOperationException($"Heading '{needle}' not found. Available (first 10): {preview}");
        }
        Heading chosen;
        if (occurrenceIndex is int idx)
        {
            if (idx < 0 || idx >= matches.Count)
                throw new ArgumentOutOfRangeException(nameof(occurrenceIndex), $"occurrenceIndex {idx} out of range 0..{matches.Count - 1}.");
            chosen = matches[idx];
        }
        else if (matches.Count == 1)
        {
            chosen = matches[0];
        }
        else
        {
            throw new InvalidOperationException($"Heading '{needle}' matched {matches.Count} times; specify occurrenceIndex (0..{matches.Count - 1}).");
        }
        var boundary = headings.FirstOrDefault(h => h.StartIndex > chosen.EndIndex && h.Level <= chosen.Level);
        return new SectionRange(chosen.StartIndex, chosen.EndIndex, boundary?.StartIndex ?? body.Length);
    }

    private static int CountOccurrences(string source, string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static readonly Dictionary<string, string> NamedEntityMap = new(StringComparer.Ordinal)
    {
        ["nbsp"] = "&#160;", ["copy"] = "&#169;", ["reg"] = "&#174;", ["trade"] = "&#8482;",
        ["mdash"] = "&#8212;", ["ndash"] = "&#8211;", ["hellip"] = "&#8230;",
        ["ldquo"] = "&#8220;", ["rdquo"] = "&#8221;", ["lsquo"] = "&#8216;", ["rsquo"] = "&#8217;",
        ["laquo"] = "&#171;", ["raquo"] = "&#187;", ["middot"] = "&#183;", ["bull"] = "&#8226;",
        ["deg"] = "&#176;", ["sect"] = "&#167;", ["para"] = "&#182;", ["times"] = "&#215;", ["divide"] = "&#247;"
    };

    private static readonly Regex NamedEntityRegex = new(@"&([A-Za-z][A-Za-z0-9]{1,31});", RegexOptions.Compiled);

    private static string ReplaceNamedEntities(string input) =>
        NamedEntityRegex.Replace(input, m =>
        {
            var name = m.Groups[1].Value;
            if (name is "amp" or "lt" or "gt" or "quot" or "apos") return m.Value;
            return NamedEntityMap.TryGetValue(name, out var numeric) ? numeric : m.Value;
        });

    private static (int Version, string SpaceKey, string Title, string Body) ParsePageContext(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var version = root.GetProperty("version").GetProperty("number").GetInt32();
        var spaceKey = root.GetProperty("space").GetProperty("key").GetString() ?? throw new InvalidOperationException("space.key missing");
        var title = root.GetProperty("title").GetString() ?? throw new InvalidOperationException("title missing");
        var body = root.GetProperty("body").GetProperty("storage").GetProperty("value").GetString() ?? "";
        return (version, spaceKey, title, body);
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
