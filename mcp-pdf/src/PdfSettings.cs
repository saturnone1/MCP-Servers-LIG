using System.Text.Json;

public sealed class PdfSettings
{
    public required string DataDirectory { get; init; }
    public required string DatabasePath { get; init; }
    public required string[] AllowedRoots { get; init; }
    public required bool WritesEnabled { get; init; }
    public required string DoclingMode { get; init; }
    public required string DoclingServiceUrl { get; init; }
    public string? DoclingApiKey { get; init; }
    public required string DoclingCommand { get; init; }
    public required bool DoclingUseAsync { get; init; }
    public required int DoclingPollIntervalSeconds { get; init; }
    public required int MaxConcurrentJobs { get; init; }
    public required int JobTimeoutSeconds { get; init; }
    public required string DefaultProfile { get; init; }
    public required string DefaultChunkProfile { get; init; }
    public required Dictionary<string, PdfProfile> Profiles { get; init; }
    public required Dictionary<string, ChunkProfile> ChunkProfiles { get; init; }
    public required string EmbeddingProvider { get; init; }
    public required string EmbeddingEndpoint { get; init; }
    public string? EmbeddingApiKey { get; init; }
    public required string EmbeddingModel { get; init; }
    public string? PostgreSqlConnectionString { get; init; }
    public required string QdrantUrl { get; init; }
    public string? QdrantApiKey { get; init; }
    public required string PdfRenderCommand { get; init; }

    public static PdfSettings Load()
    {
        var data = Environment.GetEnvironmentVariable("PDF_DATA_DIR");
        if (string.IsNullOrWhiteSpace(data))
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LIG AI MCP", "pdf");
        data = Path.GetFullPath(Environment.ExpandEnvironmentVariables(data));
        Directory.CreateDirectory(data);

        var profiles = new Dictionary<string, PdfProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["fast"] = new("fast", "off", ["kor", "eng"], "fast", false, true, false, false),
            ["balanced-ko"] = new("balanced-ko", "auto", ["kor", "eng"], "accurate", true, true, false, false),
            ["accurate-ko"] = new("accurate-ko", "auto", ["kor", "eng"], "accurate", true, true, true, true),
            ["scanned-ko"] = new("scanned-ko", "force", ["kor", "eng"], "accurate", true, true, false, false)
        };
        var chunks = new Dictionary<string, ChunkProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["rag-default"] = new("rag-default", 700, 1000, 120, true, true, true, 100, 40),
            ["rag-small"] = new("rag-small", 400, 650, 80, true, true, true, 70, 30),
            ["rag-large"] = new("rag-large", 1100, 1600, 200, true, true, true, 140, 60)
        };

        LoadProfileOverrides(Path.Combine(AppContext.BaseDirectory, "config", "profiles.json"), profiles, chunks);
        return new PdfSettings
        {
            DataDirectory = data,
            DatabasePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(Environment.GetEnvironmentVariable("PDF_JOB_DB") ?? Path.Combine(data, "mcp-pdf.db"))),
            AllowedRoots = ParseRoots(Environment.GetEnvironmentVariable("MCP_ALLOWED_DIRS")),
            WritesEnabled = !string.Equals(Environment.GetEnvironmentVariable("MCP_ENABLE_PDF_WRITES"), "false", StringComparison.OrdinalIgnoreCase),
            DoclingMode = (Environment.GetEnvironmentVariable("DOCLING_MODE") ?? "remote").Trim().ToLowerInvariant(),
            DoclingServiceUrl = (Environment.GetEnvironmentVariable("DOCLING_SERVICE_URL") ?? "http://127.0.0.1:5001").TrimEnd('/'),
            DoclingApiKey = Environment.GetEnvironmentVariable("DOCLING_SERVICE_API_KEY"),
            DoclingCommand = Environment.GetEnvironmentVariable("DOCLING_COMMAND") ?? "docling",
            DoclingUseAsync = !string.Equals(Environment.GetEnvironmentVariable("DOCLING_USE_ASYNC"), "false", StringComparison.OrdinalIgnoreCase),
            DoclingPollIntervalSeconds = ParseInt("DOCLING_POLL_INTERVAL_SECONDS", 2, 1, 60),
            MaxConcurrentJobs = ParseInt("PDF_MAX_CONCURRENT_JOBS", 2, 1, 128),
            JobTimeoutSeconds = ParseInt("PDF_JOB_TIMEOUT_SECONDS", 86400, 60, 2592000),
            DefaultProfile = Environment.GetEnvironmentVariable("PDF_DEFAULT_PROFILE") ?? "balanced-ko",
            DefaultChunkProfile = Environment.GetEnvironmentVariable("PDF_DEFAULT_CHUNK_PROFILE") ?? "rag-default",
            Profiles = profiles,
            ChunkProfiles = chunks,
            EmbeddingProvider = (Environment.GetEnvironmentVariable("PDF_EMBEDDING_PROVIDER") ?? "none").ToLowerInvariant(),
            EmbeddingEndpoint = (Environment.GetEnvironmentVariable("PDF_EMBEDDING_ENDPOINT") ?? "http://127.0.0.1:11434/v1/embeddings").Trim(),
            EmbeddingApiKey = Environment.GetEnvironmentVariable("PDF_EMBEDDING_API_KEY"),
            EmbeddingModel = Environment.GetEnvironmentVariable("PDF_EMBEDDING_MODEL") ?? "nomic-embed-text",
            PostgreSqlConnectionString = Environment.GetEnvironmentVariable("PDF_POSTGRES_CONNECTION_STRING"),
            QdrantUrl = (Environment.GetEnvironmentVariable("PDF_QDRANT_URL") ?? "http://127.0.0.1:6333").TrimEnd('/'),
            QdrantApiKey = Environment.GetEnvironmentVariable("PDF_QDRANT_API_KEY"),
            PdfRenderCommand = ResolvePdfRenderCommand(Environment.GetEnvironmentVariable("PDF_RENDER_COMMAND"))
        };
    }

    public static string ResolvePdfRenderCommand(string? configured = null, string? baseDirectory = null)
    {
        var normalizedConfiguration = configured?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedConfiguration) &&
            !string.Equals(normalizedConfiguration, "pdftoppm", StringComparison.OrdinalIgnoreCase))
            return normalizedConfiguration;

        var applicationDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        var candidates = new[]
        {
            Path.Combine(applicationDirectory, "dependencies", "poppler", "Library", "bin", "pdftoppm.exe"),
            Path.Combine(applicationDirectory, "..", "dependencies", "poppler", "Library", "bin", "pdftoppm.exe")
        };
        foreach (var candidate in candidates.Select(Path.GetFullPath))
            if (File.Exists(candidate)) return candidate;

        return string.IsNullOrWhiteSpace(normalizedConfiguration) ? "pdftoppm" : normalizedConfiguration;
    }

    public string RequireAllowedPdf(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!string.Equals(Path.GetExtension(full), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .pdf files are supported.");
        if (!File.Exists(full))
            throw new FileNotFoundException("PDF file does not exist.", full);
        if (!AllowedRoots.Any(root => IsInside(full, root)))
            throw new UnauthorizedAccessException($"Path is outside MCP_ALLOWED_DIRS: {full}");
        return full;
    }

    public void RequireWrites()
    {
        if (!WritesEnabled)
            throw new UnauthorizedAccessException("PDF writes are disabled because MCP_ENABLE_PDF_WRITES=false.");
    }

    private static void LoadProfileOverrides(string path, Dictionary<string, PdfProfile> profiles, Dictionary<string, ChunkProfile> chunks)
    {
        if (!File.Exists(path)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("profiles", out var p))
            foreach (var item in p.EnumerateArray())
            {
                var profile = item.Deserialize<PdfProfile>(JsonOptions()) ?? throw new InvalidDataException("Invalid PDF profile.");
                profiles[profile.Name] = profile;
            }
        if (document.RootElement.TryGetProperty("chunkProfiles", out var c))
            foreach (var item in c.EnumerateArray())
            {
                var profile = item.Deserialize<ChunkProfile>(JsonOptions()) ?? throw new InvalidDataException("Invalid chunk profile.");
                chunks[profile.Name] = profile;
            }
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static int ParseInt(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static string[] ParseRoots(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "*")
            return DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => Normalize(d.RootDirectory.FullName)).ToArray();
        return raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Environment.ExpandEnvironmentVariables).Select(Path.GetFullPath).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    private static bool IsInside(string path, string root) => (Path.GetFullPath(path) + (Directory.Exists(path) ? Path.DirectorySeparatorChar : "")).StartsWith(root, StringComparison.OrdinalIgnoreCase);
}
