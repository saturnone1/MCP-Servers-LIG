using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public interface IPdfParser
{
    string Name { get; }
    Task<object> HealthAsync(CancellationToken cancellationToken);
    Task<ParsedDocument> ParseAsync(string pdfPath, PdfProfile profile, string artifactDirectory, CancellationToken cancellationToken);
}

public static class PdfParserFactory
{
    public static IPdfParser Create(PdfSettings settings) => settings.DoclingMode switch
    {
        "local" => new LocalDoclingParser(settings),
        "remote" => new RemoteDoclingParser(settings),
        _ => throw new InvalidOperationException("DOCLING_MODE must be 'local' or 'remote'.")
    };
}

public sealed class RemoteDoclingParser : IPdfParser
{
    private readonly PdfSettings _settings;
    private readonly HttpClient _client = new() { Timeout = Timeout.InfiniteTimeSpan };
    public string Name => "docling-serve";

    public RemoteDoclingParser(PdfSettings settings)
    {
        _settings = settings;
        if (!string.IsNullOrWhiteSpace(settings.DoclingApiKey))
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.DoclingApiKey);
    }

    public async Task<object> HealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetAsync(_settings.DoclingServiceUrl + "/health", cancellationToken);
            return new { available = response.IsSuccessStatusCode, mode = "remote", url = _settings.DoclingServiceUrl, statusCode = (int)response.StatusCode };
        }
        catch (Exception exception)
        {
            return new { available = false, mode = "remote", url = _settings.DoclingServiceUrl, error = exception.Message };
        }
    }

    public async Task<ParsedDocument> ParseAsync(string pdfPath, PdfProfile profile, string artifactDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(artifactDirectory);
        var responseText = await ConvertAsync(pdfPath, profile, cancellationToken);
        var envelope = JsonSerializer.Deserialize<DoclingResultEnvelope>(responseText, JsonDefaults.Options)
            ?? throw new InvalidDataException("Docling Serve returned an empty response.");
        if (envelope.Document?.Json is null || envelope.Status is "failure" or "skipped")
            throw new InvalidDataException($"Docling conversion failed with status '{envelope.Status}': {JsonSerializer.Serialize(envelope.Errors)}");
        var rawJson = envelope.Document.Json is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(envelope.Document.Json, JsonDefaults.Options);
        await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "docling-document.json"), rawJson, cancellationToken);
        if (!string.IsNullOrWhiteSpace(envelope.Document.Markdown))
            await File.WriteAllTextAsync(Path.Combine(artifactDirectory, "document.md"), envelope.Document.Markdown, cancellationToken);
        var parsed = DoclingNormalizer.Normalize(rawJson, envelope.Document.Markdown, Path.GetFileNameWithoutExtension(pdfPath), artifactDirectory, envelope.Status, envelope.ProcessingTime, profile.OcrMode == "force");
        if (envelope.Errors.Length > 0)
            parsed = parsed with { Warnings = parsed.Warnings.Concat(envelope.Errors.Select(error => new ProcessingWarning("docling_error", Limit(JsonSerializer.Serialize(error, JsonDefaults.Options), 4000), null, "error"))).ToArray() };
        return parsed;
    }

    private async Task<string> ConvertAsync(string pdfPath, PdfProfile profile, CancellationToken cancellationToken)
    {
        if (!_settings.DoclingUseAsync) return await SendFileAsync(pdfPath, profile, "/v1/convert/file", cancellationToken);
        try
        {
            var taskText = await SendFileAsync(pdfPath, profile, "/v1/convert/file/async", cancellationToken);
            using var taskDocument = JsonDocument.Parse(taskText);
            if (!taskDocument.RootElement.TryGetProperty("task_id", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString()))
                throw new InvalidDataException("Docling async submission did not return task_id.");
            var taskId = Uri.EscapeDataString(idElement.GetString()!);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var statusResponse = await _client.GetAsync(_settings.DoclingServiceUrl + "/v1/status/poll/" + taskId, cancellationToken);
                var statusText = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!statusResponse.IsSuccessStatusCode) throw new InvalidOperationException($"Docling task polling returned HTTP {(int)statusResponse.StatusCode}: {Limit(statusText, 4000)}");
                using var statusDocument = JsonDocument.Parse(statusText);
                var status = statusDocument.RootElement.TryGetProperty("task_status", out var statusElement) ? statusElement.GetString()?.ToLowerInvariant() : null;
                if (status == "success") break;
                if (status == "failure") throw new InvalidOperationException($"Docling async conversion failed: {Limit(statusText, 4000)}");
                await Task.Delay(TimeSpan.FromSeconds(_settings.DoclingPollIntervalSeconds), cancellationToken);
            }
            using var resultResponse = await _client.GetAsync(_settings.DoclingServiceUrl + "/v1/result/" + taskId, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var resultText = await resultResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!resultResponse.IsSuccessStatusCode) throw new InvalidOperationException($"Docling result returned HTTP {(int)resultResponse.StatusCode}: {Limit(resultText, 4000)}");
            return resultText;
        }
        catch (DoclingEndpointNotFoundException)
        {
            return await SendFileAsync(pdfPath, profile, "/v1/convert/file", cancellationToken);
        }
    }

    private async Task<string> SendFileAsync(string pdfPath, PdfProfile profile, string endpoint, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(pdfPath);
        using var form = new MultipartFormDataContent();
        using var content = new StreamContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, "files", Path.GetFileName(pdfPath));
        form.Add(new StringContent("pdf"), "from_formats");
        form.Add(new StringContent("json"), "to_formats");
        form.Add(new StringContent(profile.OcrMode == "off" ? "false" : "true"), "do_ocr");
        form.Add(new StringContent(profile.OcrMode == "force" ? "true" : "false"), "force_ocr");
        form.Add(new StringContent(profile.ExtractTables ? "true" : "false"), "do_table_structure");
        form.Add(new StringContent(profile.TableMode), "table_mode");
        form.Add(new StringContent(profile.ExtractImages ? "embedded" : "placeholder"), "image_export_mode");
        form.Add(new StringContent(profile.ExtractImages ? "true" : "false"), "generate_picture_images");
        form.Add(new StringContent(profile.EnrichCode ? "true" : "false"), "do_code_enrichment");
        form.Add(new StringContent(profile.EnrichFormulas ? "true" : "false"), "do_formula_enrichment");
        foreach (var language in profile.OcrLanguages) form.Add(new StringContent(language), "ocr_lang");
        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.DoclingServiceUrl + endpoint) { Content = form };
        if (!string.IsNullOrWhiteSpace(_settings.DoclingApiKey)) request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.DoclingApiKey);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed) && endpoint.EndsWith("/async", StringComparison.Ordinal))
            throw new DoclingEndpointNotFoundException();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Docling Serve returned HTTP {(int)response.StatusCode}: {Limit(responseText, 4000)}");
        return responseText;
    }

    private static string Limit(string value, int count) => value.Length <= count ? value : value[..count];
    private sealed class DoclingEndpointNotFoundException : Exception;
}

public sealed class LocalDoclingParser : IPdfParser
{
    private readonly PdfSettings _settings;
    public string Name => "docling-cli";
    public LocalDoclingParser(PdfSettings settings) => _settings = settings;

    public async Task<object> HealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunAsync(["--version"], Environment.CurrentDirectory, TimeSpan.FromSeconds(15), cancellationToken);
            return new { available = result.ExitCode == 0, mode = "local", command = _settings.DoclingCommand, version = result.Stdout.Trim(), error = result.Stderr.Trim() };
        }
        catch (Exception exception)
        {
            return new { available = false, mode = "local", command = _settings.DoclingCommand, error = exception.Message };
        }
    }

    public async Task<ParsedDocument> ParseAsync(string pdfPath, PdfProfile profile, string artifactDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(artifactDirectory);
        var args = new List<string> { "convert", pdfPath, "--to", "json", "--output", artifactDirectory, profile.ExtractTables ? "--tables" : "--no-tables", "--table-mode", profile.TableMode, "--image-export-mode", profile.ExtractImages ? "referenced" : "placeholder" };
        args.Add(profile.OcrMode == "off" ? "--no-ocr" : "--ocr");
        if (profile.OcrMode == "force") args.Add("--force-ocr");
        if (profile.OcrLanguages.Length > 0) args.AddRange(["--ocr-lang", string.Join(',', profile.OcrLanguages)]);
        if (profile.EnrichCode) args.Add("--enrich-code");
        if (profile.EnrichFormulas) args.Add("--enrich-formula");
        var started = Stopwatch.StartNew();
        var result = await RunAsync(args, artifactDirectory, TimeSpan.FromSeconds(_settings.JobTimeoutSeconds), cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Docling CLI failed ({result.ExitCode}): {result.Stderr}");
        var jsonPath = Directory.GetFiles(artifactDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            ?? throw new FileNotFoundException("Docling CLI did not create a JSON document.");
        var rawJson = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        return DoclingNormalizer.Normalize(rawJson, "", Path.GetFileNameWithoutExtension(pdfPath), artifactDirectory, "success", started.Elapsed.TotalSeconds, profile.OcrMode == "force");
    }

    private async Task<CommandResult> RunAsync(IReadOnlyList<string> args, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var start = new ProcessStartInfo(_settings.DoclingCommand) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start Docling command: {_settings.DoclingCommand}");
        var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try { await process.WaitForExitAsync(timeoutSource.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw; }
        return new(process.ExitCode, await stdout, await stderr);
    }

    private sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
}

public static class DoclingNormalizer
{
    public static ParsedDocument Normalize(string rawJson, string markdown, string fallbackTitle, string artifactDirectory, string status, double seconds, bool forcedOcr = false)
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        var title = StringProperty(root, "name") ?? StringProperty(root, "title") ?? fallbackTitle;
        var elements = new List<ParsedElement>();
        var artifacts = new List<ParsedArtifact>();
        var warnings = new List<ProcessingWarning>();
        var headingLevels = new Dictionary<string, int>();
        var documentOrder = GetDocumentOrder(root);
        var order = 0;

        foreach (var (arrayName, defaultType) in new[] { ("texts", "paragraph"), ("tables", "table"), ("pictures", "figure"), ("key_value_items", "key_value") })
        {
            if (!root.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array) continue;
            var itemIndex = 0;
            foreach (var item in array.EnumerateArray())
            {
                var type = StringProperty(item, "label") ?? defaultType;
                var text = ExtractElementText(item, type);
                var (page, bbox, confidence) = Provenance(item);
                var elementId = StableId("element", $"{arrayName}|{order}|{page}|{text}");
                var readingOrder = documentOrder.GetValueOrDefault($"#/{arrayName}/{itemIndex}", 1_000_000 + order);
                elements.Add(new(elementId, NormalizeType(type), text, [], page, page, bbox, readingOrder, StringProperty(item, "caption"), confidence, type == "table" ? ExtractTableJson(item) : null));
                if (type.Contains("section_header", StringComparison.OrdinalIgnoreCase) || type.Equals("title", StringComparison.OrdinalIgnoreCase)) headingLevels[elementId] = HeadingLevel(item);
                order++;
                itemIndex++;
                if (defaultType == "figure")
                {
                    var imagePath = FindImagePath(item, artifactDirectory);
                    if (imagePath is not null)
                    {
                        var artifactId = StableId("artifact", $"{elementId}|{imagePath}");
                        artifacts.Add(new(artifactId, "image", imagePath, page, StringProperty(item, "caption"), Mime(imagePath)));
                    }
                }
            }
        }

        if (elements.Count > 0)
        {
            var ordered = documentOrder.Count > 0
                ? elements.OrderBy(e => e.ReadingOrder).ToArray()
                : elements.OrderBy(e => e.PageStart).ThenByDescending(e => e.BoundingBox is { Length: > 1 } ? e.BoundingBox[1] : double.MinValue).ThenBy(e => e.ReadingOrder).ToArray();
            var headings = new List<string>();
            elements = ordered.Select((element, index) =>
            {
                if (headingLevels.TryGetValue(element.ElementId, out var level) && !string.IsNullOrWhiteSpace(element.Text)) UpdateHeadings(headings, element.Text, level);
                return element with { HeadingPath = headings.ToArray(), ReadingOrder = index };
            }).ToList();
        }

        if (elements.Count == 0 && !string.IsNullOrWhiteSpace(markdown))
            elements.AddRange(FromMarkdown(markdown));
        if (elements.Count == 0)
            throw new InvalidDataException("Docling result did not contain readable document elements.");

        var pageCount = PageCount(root, elements);
        var pages = Enumerable.Range(1, pageCount).Select(pageNumber =>
        {
            var pageText = string.Join("\n\n", elements.Where(e => e.PageStart == pageNumber && !string.IsNullOrWhiteSpace(e.Text)).OrderBy(e => e.ReadingOrder).Select(e => e.Text));
            var confidenceValues = elements.Where(e => e.PageStart == pageNumber && e.Confidence.HasValue).Select(e => e.Confidence!.Value).ToArray();
            return new ParsedPage(pageNumber, pageText, forcedOcr, confidenceValues.Length == 0 ? null : confidenceValues.Average(), string.IsNullOrWhiteSpace(pageText) ? "empty" : "parsed");
        }).ToArray();
        foreach (var page in pages.Where(p => string.IsNullOrWhiteSpace(p.Text)))
            warnings.Add(new("empty_page", "No readable text was extracted from this page.", page.PageNumber));
        if (status == "partial_success") warnings.Add(new("docling_partial", "Docling reported partial success."));
        return new(title, pageCount, elements.OrderBy(e => e.PageStart).ThenBy(e => e.ReadingOrder).ToArray(), pages, artifacts, warnings, "docling", "v1", seconds);
    }

    private static IEnumerable<ParsedElement> FromMarkdown(string markdown)
    {
        var headings = new List<string>();
        var order = 0;
        foreach (var block in Regex.Split(markdown, @"\r?\n\s*\r?\n").Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            var trimmed = block.Trim();
            var match = Regex.Match(trimmed, @"^(#{1,6})\s+(.+)");
            var type = match.Success ? "section_header" : trimmed.StartsWith('|') ? "table" : "paragraph";
            var text = match.Success ? match.Groups[2].Value.Trim() : trimmed;
            if (match.Success) UpdateHeadings(headings, text, match.Groups[1].Value.Length);
            yield return new(StableId("element", $"markdown|{order}|{text}"), type, text, headings.ToArray(), 1, 1, null, order++, null, null, type == "table" ? JsonSerializer.Serialize(new { markdown = text }) : null);
        }
    }

    private static string ExtractElementText(JsonElement item, string type)
    {
        foreach (var name in new[] { "text", "orig", "content", "caption" })
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
        if (type.Contains("table", StringComparison.OrdinalIgnoreCase) && item.TryGetProperty("data", out var data))
            return TableToMarkdown(data);
        return "";
    }

    private static string TableToMarkdown(JsonElement data)
    {
        if (!data.TryGetProperty("table_cells", out var cells) || cells.ValueKind != JsonValueKind.Array) return data.GetRawText();
        var rows = new SortedDictionary<int, SortedDictionary<int, string>>();
        foreach (var cell in cells.EnumerateArray())
        {
            var row = IntProperty(cell, "start_row_offset_idx") ?? IntProperty(cell, "row") ?? 0;
            var col = IntProperty(cell, "start_col_offset_idx") ?? IntProperty(cell, "column") ?? 0;
            if (!rows.TryGetValue(row, out var columns)) rows[row] = columns = [];
            columns[col] = StringProperty(cell, "text") ?? "";
        }
        if (rows.Count == 0) return data.GetRawText();
        var width = rows.Values.SelectMany(r => r.Keys).DefaultIfEmpty(0).Max() + 1;
        var lines = rows.Select(row => "| " + string.Join(" | ", Enumerable.Range(0, width).Select(c => row.Value.GetValueOrDefault(c, ""))) + " |").ToList();
        lines.Insert(1, "| " + string.Join(" | ", Enumerable.Repeat("---", width)) + " |");
        return string.Join("\n", lines);
    }

    private static (int Page, double[]? Bbox, double? Confidence) Provenance(JsonElement item)
    {
        if (!item.TryGetProperty("prov", out var prov) || prov.ValueKind != JsonValueKind.Array || prov.GetArrayLength() == 0) return (1, null, null);
        var first = prov[0];
        var page = IntProperty(first, "page_no") ?? IntProperty(first, "page") ?? 1;
        double[]? bbox = null;
        if (first.TryGetProperty("bbox", out var box))
        {
            if (box.ValueKind == JsonValueKind.Object)
                bbox = [DoubleProperty(box, "l") ?? 0, DoubleProperty(box, "t") ?? 0, DoubleProperty(box, "r") ?? 0, DoubleProperty(box, "b") ?? 0];
            else if (box.ValueKind == JsonValueKind.Array) bbox = box.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        }
        return (Math.Max(1, page), bbox, DoubleProperty(first, "confidence") ?? DoubleProperty(item, "confidence"));
    }

    private static int PageCount(JsonElement root, List<ParsedElement> elements)
    {
        if (root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Object) return Math.Max(1, pages.EnumerateObject().Count());
        return Math.Max(1, elements.Max(e => e.PageEnd));
    }

    private static Dictionary<string, int> GetDocumentOrder(JsonElement root)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!root.TryGetProperty("body", out var body)) return result;
        var visitedGroups = new HashSet<string>(StringComparer.Ordinal);
        Walk(body);
        return result;

        void Walk(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray()) Walk(child);
                return;
            }
            if (node.ValueKind != JsonValueKind.Object) return;
            if (node.TryGetProperty("$ref", out var reference) && reference.ValueKind == JsonValueKind.String)
            {
                var path = reference.GetString()!;
                if (path.StartsWith("#/groups/", StringComparison.Ordinal) && visitedGroups.Add(path) && ResolveReference(root, path) is JsonElement group) Walk(group);
                else if (!result.ContainsKey(path)) result[path] = result.Count;
                return;
            }
            if (node.TryGetProperty("children", out var children)) Walk(children);
        }
    }

    private static JsonElement? ResolveReference(JsonElement root, string path)
    {
        var parts = path.TrimStart('#', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var property)) current = property;
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index) && index >= 0 && index < current.GetArrayLength()) current = current[index];
            else return null;
        }
        return current;
    }

    private static int HeadingLevel(JsonElement item) => IntProperty(item, "level") ?? 1;
    private static void UpdateHeadings(List<string> headings, string text, int level)
    {
        level = Math.Clamp(level, 1, 6);
        while (headings.Count >= level) headings.RemoveAt(headings.Count - 1);
        while (headings.Count < level - 1) headings.Add("");
        headings.Add(text);
    }

    private static string NormalizeType(string type) => type.ToLowerInvariant() switch
    {
        "section_header" or "heading" => "heading", "text" => "paragraph", "picture" => "figure", "list_item" => "list", "formula" => "formula", "code" => "code", "footnote" => "footnote", var value => value
    };
    private static string? FindImagePath(JsonElement item, string directory)
    {
        foreach (var name in new[] { "image", "uri", "path" })
            if (item.TryGetProperty(name, out var value))
            {
                var candidate = value.ValueKind == JsonValueKind.String ? value.GetString() :
                    value.ValueKind == JsonValueKind.Object && value.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String ? uri.GetString() : null;
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                if (candidate.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = candidate.IndexOf(',');
                    if (comma < 0 || !candidate[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var bytes = Convert.FromBase64String(candidate[(comma + 1)..]);
                        var mediaType = candidate[5..comma].Split(';')[0].ToLowerInvariant();
                        var extension = mediaType switch { "image/png" => ".png", "image/jpeg" => ".jpg", "image/webp" => ".webp", "image/gif" => ".gif", _ => ".bin" };
                        var imageDirectory = Path.Combine(directory, "images");
                        Directory.CreateDirectory(imageDirectory);
                        var path = Path.Combine(imageDirectory, "image-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()[..20] + extension);
                        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
                        return path;
                    }
                    catch (FormatException) { continue; }
                }
                var full = Path.IsPathRooted(candidate) ? candidate : Path.Combine(directory, candidate);
                if (File.Exists(full)) return Path.GetFullPath(full);
            }
        return null;
    }
    private static string? ExtractTableJson(JsonElement item) => item.TryGetProperty("data", out var data) ? data.GetRawText() : null;
    private static string? Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => null };
    private static string StableId(string prefix, string value) => $"{prefix}_{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..20]}";
    private static string? StringProperty(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? IntProperty(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static double? DoubleProperty(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true, WriteIndented = true };
}
