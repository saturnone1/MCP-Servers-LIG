using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

public sealed class PdfRuntime : IDisposable
{
    public static PdfRuntime Current { get; private set; } = null!;
    public PdfSettings Settings { get; }
    public PdfStore Store { get; }
    public IPdfParser Parser { get; }
    public PdfChunker Chunker { get; } = new();
    public DatasetExporter Exporter { get; } = new();
    public IEmbeddingProvider Embeddings { get; }
    public PostgreSqlDatasetWriter PostgreSql { get; }
    public QdrantDatasetWriter Qdrant { get; }
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<Task> _workers = [];

    private PdfRuntime(PdfSettings settings)
    {
        Settings = settings;
        Store = new PdfStore(settings.DatabasePath);
        Parser = PdfParserFactory.Create(settings);
        Embeddings = new OpenAiCompatibleEmbeddingProvider(settings);
        PostgreSql = new PostgreSqlDatasetWriter(settings);
        Qdrant = new QdrantDatasetWriter(settings);
    }

    public static async Task<PdfRuntime> CreateAsync(PdfSettings settings)
    {
        var runtime = new PdfRuntime(settings);
        await runtime.Store.InitializeAsync();
        Current = runtime;
        for (var index = 0; index < settings.MaxConcurrentJobs; index++) runtime._workers.Add(Task.Run(runtime.WorkerLoopAsync));
        foreach (var job in await runtime.Store.ListJobsAsync(100000, JobStatus.Queued.ToString())) await runtime._queue.Writer.WriteAsync(job.JobId);
        return runtime;
    }

    public object Health() => new
    {
        dataDirectory = Settings.DataDirectory,
        database = Settings.DatabasePath,
        parser = Parser.Name,
        doclingMode = Settings.DoclingMode,
        doclingAsync = Settings.DoclingUseAsync,
        workers = Settings.MaxConcurrentJobs,
        writesEnabled = Settings.WritesEnabled,
        embeddingProvider = Settings.EmbeddingProvider
    };

    public async Task<object> ConfigAsync()
    {
        var parserHealth = await Parser.HealthAsync(CancellationToken.None);
        return new
        {
            server = "mcp-pdf", port = 42199, scope = "PDF ingest, dataset management, and evidence retrieval; no RAG answer generation",
            Health = Health(), parserHealth, allowedDirectories = Settings.AllowedRoots,
            profiles = Settings.Profiles.Values, chunkProfiles = Settings.ChunkProfiles.Values,
            optional = new { embeddings = Embeddings.Available, postgresql = PostgreSql.Available, qdrant = Settings.QdrantUrl, parquet = true, renderer = Settings.PdfRenderCommand }
        };
    }

    public async Task<object> StartIngestAsync(string source, string? profile, string? chunkProfile, bool force, bool generateEmbeddings, string? indexTarget)
    {
        Settings.RequireWrites();
        var path = Settings.RequireAllowedPdf(source);
        profile = string.IsNullOrWhiteSpace(profile) ? Settings.DefaultProfile : profile;
        chunkProfile = string.IsNullOrWhiteSpace(chunkProfile) ? Settings.DefaultChunkProfile : chunkProfile;
        if (!Settings.Profiles.ContainsKey(profile)) throw new ArgumentException($"Unknown PDF profile: {profile}");
        if (!Settings.ChunkProfiles.ContainsKey(chunkProfile)) throw new ArgumentException($"Unknown chunk profile: {chunkProfile}");
        var jobId = "job_" + Guid.NewGuid().ToString("N");
        var request = new IngestRequest(path, profile, chunkProfile, force, generateEmbeddings, indexTarget);
        var now = DateTimeOffset.UtcNow;
        await Store.CreateJobAsync(new(jobId, null, path, profile, chunkProfile, JobStatus.Queued, 0, "queued", 0, 0, 0, null, now, now), request);
        await _queue.Writer.WriteAsync(jobId);
        return new { jobId, status = "queued", source = path, profile, chunkProfile };
    }

    public async Task<object[]> CheckChangesAsync(string? documentId)
    {
        var documents = string.IsNullOrWhiteSpace(documentId)
            ? await Store.ListDocumentsAsync(null, 100000)
            : [await Store.GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}")];
        var results = new List<object>();
        foreach (var document in documents)
        {
            if (!File.Exists(document.SourcePath))
            {
                results.Add(new { document.DocumentId, document.SourcePath, state = "missing", changed = true, storedSha256 = document.Sha256, currentSha256 = (string?)null });
                continue;
            }
            var currentHash = await HashFileAsync(document.SourcePath, CancellationToken.None);
            results.Add(new { document.DocumentId, document.SourcePath, state = currentHash == document.Sha256 ? "unchanged" : "changed", changed = currentHash != document.Sha256, storedSha256 = document.Sha256, currentSha256 = currentHash });
        }
        return results.ToArray();
    }

    public async Task<object> CancelJobAsync(string jobId)
    {
        Settings.RequireWrites();
        var job = await Store.GetJobAsync(jobId) ?? throw new KeyNotFoundException($"Job not found: {jobId}");
        if (job.Status is JobStatus.Completed or JobStatus.Partial or JobStatus.Failed or JobStatus.Canceled) return new { jobId, status = job.Status.ToString() };
        await Store.UpdateJobAsync(jobId, JobStatus.CancelRequested, job.Progress, "cancel_requested", job.ProcessedPages, job.TotalPages, job.ChunksCreated);
        if (_cancellations.TryGetValue(jobId, out var cancellation)) cancellation.Cancel();
        return new { jobId, status = "cancel_requested" };
    }

    public async Task<object> RechunkAsync(string documentId, string chunkProfile)
    {
        Settings.RequireWrites();
        if (!Settings.ChunkProfiles.TryGetValue(chunkProfile, out var profile)) throw new ArgumentException($"Unknown chunk profile: {chunkProfile}");
        var document = await Store.GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        var elements = await Store.GetElementsAsync(documentId);
        var pages = await Store.ReadPagesAsync(documentId, 1, document.PageCount);
        var parsed = new ParsedDocument(document.Title, document.PageCount, elements, pages, await Store.ListArtifactsAsync(documentId), [], "stored", "1", 0);
        var parserProfile = Settings.Profiles.GetValueOrDefault(Settings.DefaultProfile) ?? Settings.Profiles.Values.First();
        var chunks = Chunker.Chunk(document, parsed, parserProfile, profile);
        await Store.ReplaceChunksAsync(documentId, chunks);
        return new { documentId, chunkProfile, chunks = chunks.Count, reparsed = false };
    }

    public async Task<object> GenerateEmbeddingsAsync(string documentId)
    {
        Settings.RequireWrites();
        var chunks = await Store.ListChunksAsync(documentId, 0, 1000000);
        if (chunks.Length == 0) throw new InvalidOperationException("Document has no chunks.");
        await ApplyEmbeddingsAsync(chunks, CancellationToken.None);
        return new { documentId, model = Settings.EmbeddingModel, vectors = chunks.Length, dimensions = chunks[0].Embedding?.Length ?? 0 };
    }

    public async Task<SearchResult[]> VectorSearchAsync(string query, string? documentId, int limit)
    {
        var queryVector = (await Embeddings.EmbedAsync([query], CancellationToken.None))[0];
        var chunks = await Store.GetEmbeddedChunksAsync(documentId);
        return chunks.Select(chunk => new SearchResult(chunk.ChunkId, chunk.DocumentId, chunk.Title, chunk.Text, chunk.HeadingPath, chunk.PageStart, chunk.PageEnd, chunk.ContentType, Cosine(queryVector, chunk.Embedding!)))
            .OrderByDescending(result => result.Score).Take(Math.Clamp(limit, 1, 100)).ToArray();
    }

    public async Task<SearchResult[]> HybridSearchAsync(string query, string? documentId, int limit)
    {
        var keyword = await Store.KeywordSearchAsync(query, documentId, Math.Clamp(limit * 3, 10, 100));
        if (!Embeddings.Available) return keyword.Take(limit).ToArray();
        var vectors = await VectorSearchAsync(query, documentId, Math.Clamp(limit * 3, 10, 100));
        return keyword.Select((item, rank) => (item, score: 1d / (60 + rank)))
            .Concat(vectors.Select((item, rank) => (item, score: 1d / (60 + rank))))
            .GroupBy(item => item.item.ChunkId).Select(group => group.OrderByDescending(x => x.score).First().item with { Score = group.Sum(x => x.score) })
            .OrderByDescending(item => item.Score).Take(limit).ToArray();
    }

    public async Task<ValidationReport> ValidateAsync(string documentId)
    {
        var document = await Store.GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        var pages = await Store.ReadPagesAsync(documentId, 1, document.PageCount);
        var chunks = await Store.ListChunksAsync(documentId, 0, 1000000);
        var warnings = await Store.GetWarningsAsync(documentId);
        var duplicateGroups = chunks.GroupBy(c => Normalize(c.Text)).Where(g => g.Key.Length > 20 && g.Count() > 1).Sum(g => g.Count() - 1);
        var issues = new List<string>();
        var empty = pages.Count(p => string.IsNullOrWhiteSpace(p.Text));
        var shortChunks = chunks.Count(c => c.TokenCount < 40);
        var oversized = chunks.Count(c => c.TokenCount > Settings.ChunkProfiles.GetValueOrDefault(c.ChunkProfile)?.MaxTokens);
        if (empty > 0) issues.Add($"{empty} pages contain no extracted text.");
        if (shortChunks > 0) issues.Add($"{shortChunks} chunks are shorter than 40 estimated tokens.");
        if (oversized > 0) issues.Add($"{oversized} chunks exceed their configured token ceiling.");
        if (duplicateGroups > 0) issues.Add($"{duplicateGroups} duplicate chunks were detected.");
        return new(documentId, document.PageCount, chunks.Length, empty, shortChunks, oversized, pages.Count(p => p.OcrApplied), warnings.Length, chunks.Length == 0 ? 0 : (double)duplicateGroups / chunks.Length, issues.ToArray());
    }

    public async Task<object> RenderPagesAsync(string documentId, string pages, int dpi)
    {
        var document = await Store.GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        var numbers = PageRanges.Parse(pages, document.PageCount);
        var outputDirectory = Path.Combine(Settings.DataDirectory, "documents", documentId, $"v{document.CurrentVersion}", "rendered");
        Directory.CreateDirectory(outputDirectory);
        var outputs = new List<string>();
        foreach (var page in numbers)
        {
            var prefix = Path.Combine(outputDirectory, $"page-{page:D6}");
            var args = new[] { "-f", page.ToString(), "-l", page.ToString(), "-singlefile", "-png", "-r", Math.Clamp(dpi, 72, 600).ToString(), document.SourcePath, prefix };
            var result = await RunCommandAsync(Settings.PdfRenderCommand, args, outputDirectory, TimeSpan.FromMinutes(10));
            if (result.ExitCode != 0) throw new InvalidOperationException($"PDF renderer failed: {result.Stderr}");
            var path = prefix + ".png";
            if (!File.Exists(path)) throw new FileNotFoundException("PDF renderer did not create the expected PNG.", path);
            outputs.Add(path);
        }
        return new { documentId, pages = numbers, dpi, images = outputs };
    }

    public async Task<object> ExportAsync(string documentId, string format, string? destination)
    {
        var document = await Store.GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        var chunks = await Store.ListChunksAsync(documentId, 0, 1000000);
        var directory = string.IsNullOrWhiteSpace(destination) ? Path.Combine(Settings.DataDirectory, "exports", documentId) : Path.GetFullPath(Environment.ExpandEnvironmentVariables(destination));
        Directory.CreateDirectory(directory);
        return format.ToLowerInvariant() switch
        {
            "jsonl" => await Exporter.ExportJsonlAsync(Path.Combine(directory, $"{document.FileName}.chunks.jsonl"), chunks, CancellationToken.None),
            "parquet" => await Exporter.ExportParquetAsync(Path.Combine(directory, $"{document.FileName}.chunks.parquet"), chunks, CancellationToken.None),
            _ => throw new ArgumentException("format must be jsonl or parquet")
        };
    }

    public async Task<object> SaveDatasetAsync(string documentId, string provider, string? target)
    {
        Settings.RequireWrites();
        var document = await Store.GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        var chunks = await Store.ListChunksAsync(documentId, 0, 1000000);
        var normalizedProvider = provider.ToLowerInvariant();
        var normalizedTarget = normalizedProvider switch { "sqlite" => Settings.DatabasePath, "postgresql" => "configured-connection", "qdrant" => string.IsNullOrWhiteSpace(target) ? "mcp_pdf_chunks" : target, _ => target ?? "" };
        var operationId = "index_" + Guid.NewGuid().ToString("N");
        await Store.RecordIndexOperationAsync(operationId, documentId, document.CurrentVersion, normalizedProvider, normalizedTarget, "running", 0);
        try
        {
            var result = normalizedProvider switch
            {
                "sqlite" => (object)new { provider = "sqlite", records = chunks.Length, database = Settings.DatabasePath },
                "postgresql" => await PostgreSql.UpsertAsync(chunks, CancellationToken.None),
                "qdrant" => await Qdrant.UpsertAsync(normalizedTarget, chunks, CancellationToken.None),
                _ => throw new ArgumentException("provider must be sqlite, postgresql, or qdrant")
            };
            await Store.RecordIndexOperationAsync(operationId, documentId, document.CurrentVersion, normalizedProvider, normalizedTarget, "completed", chunks.Length);
            return result;
        }
        catch (Exception exception)
        {
            await Store.RecordIndexOperationAsync(operationId, documentId, document.CurrentVersion, normalizedProvider, normalizedTarget, "failed", 0, exception.Message);
            throw;
        }
    }

    private async Task WorkerLoopAsync()
    {
        await foreach (var jobId in _queue.Reader.ReadAllAsync(_shutdown.Token))
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            cancellation.CancelAfter(TimeSpan.FromSeconds(Settings.JobTimeoutSeconds));
            if (!_cancellations.TryAdd(jobId, cancellation)) continue;
            try { await ProcessJobAsync(jobId, cancellation.Token); }
            catch (OperationCanceledException)
            {
                var job = await Store.GetJobAsync(jobId);
                await Store.UpdateJobAsync(jobId, JobStatus.Canceled, job?.Progress ?? 0, "canceled", job?.ProcessedPages ?? 0, job?.TotalPages ?? 0, job?.ChunksCreated ?? 0, "Job canceled.");
                await Store.AddEventAsync(jobId, "warning", "canceled", "Job canceled.");
            }
            catch (Exception exception)
            {
                var job = await Store.GetJobAsync(jobId);
                await Store.UpdateJobAsync(jobId, JobStatus.Failed, job?.Progress ?? 0, "failed", job?.ProcessedPages ?? 0, job?.TotalPages ?? 0, job?.ChunksCreated ?? 0, exception.ToString());
                await Store.AddEventAsync(jobId, "error", "failed", exception.Message);
            }
            finally { _cancellations.TryRemove(jobId, out _); cancellation.Dispose(); }
        }
    }

    private async Task ProcessJobAsync(string jobId, CancellationToken cancellationToken)
    {
        var request = await Store.GetJobRequestAsync(jobId) ?? throw new InvalidDataException("Job request is missing.");
        var path = Settings.RequireAllowedPdf(request.Source);
        await Store.UpdateJobAsync(jobId, JobStatus.Inspecting, 2, "hashing");
        var hash = await HashFileAsync(path, cancellationToken);
        var existing = await Store.FindExistingAsync(hash, request.Profile, request.ChunkProfile);
        if (existing is not null && !request.Force)
        {
            await Store.UpdateJobAsync(jobId, JobStatus.Completed, 100, "deduplicated", existing.PageCount, existing.PageCount, (await Store.ListChunksAsync(existing.DocumentId, 0, 1000000)).Length, documentId: existing.DocumentId);
            await Store.AddEventAsync(jobId, "info", "deduplicated", "Existing processed document reused.");
            return;
        }
        var previous = await Store.FindBySourceAsync(path, request.Profile, request.ChunkProfile);
        var documentId = previous?.DocumentId ?? StableId("doc", $"{path}|{request.Profile}|{request.ChunkProfile}");
        var version = previous is null ? 1 : previous.CurrentVersion + 1;
        var artifactDirectory = Path.Combine(Settings.DataDirectory, "documents", documentId, $"v{version}");
        Directory.CreateDirectory(artifactDirectory);
        await Store.UpdateJobAsync(jobId, JobStatus.Parsing, 8, "parsing", documentId: documentId);
        await Store.AddEventAsync(jobId, "info", "parsing", $"Parsing with {Parser.Name}.");
        var parsed = await Parser.ParseAsync(path, Settings.Profiles[request.Profile], artifactDirectory, cancellationToken);
        await Store.UpdateJobAsync(jobId, JobStatus.Normalizing, 65, "normalizing", parsed.PageCount, parsed.PageCount, documentId: documentId);
        var now = DateTimeOffset.UtcNow;
        var document = new DocumentRecord(documentId, path, Path.GetFileName(path), hash, new FileInfo(path).Length, parsed.Title, parsed.PageCount, version, "processing", previous?.CreatedAt ?? now, now);
        await Store.UpdateJobAsync(jobId, JobStatus.Chunking, 72, "chunking", parsed.PageCount, parsed.PageCount, documentId: documentId);
        var chunks = Chunker.Chunk(document, parsed, Settings.Profiles[request.Profile], Settings.ChunkProfiles[request.ChunkProfile]).ToArray();
        if (request.GenerateEmbeddings)
        {
            await Store.UpdateJobAsync(jobId, JobStatus.Embedding, 82, "embedding", parsed.PageCount, parsed.PageCount, chunks.Length, documentId: documentId);
            await ApplyEmbeddingsAsync(chunks, cancellationToken, save: false);
        }
        var finalDocument = document with { Status = parsed.Warnings.Any(w => w.Severity == "error") ? "partial" : "completed", UpdatedAt = DateTimeOffset.UtcNow };
        var manifestPath = Path.Combine(artifactDirectory, "ingest-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { document = finalDocument, request, parsed.Parser, parsed.ParserVersion, parsed.PageCount, chunks = chunks.Length, parsed.Warnings }, JsonDefaults.Options), cancellationToken);
        await Store.SaveDocumentAsync(finalDocument, parsed, request.Profile, request.ChunkProfile, chunks, manifestPath);
        if (!string.IsNullOrWhiteSpace(request.IndexTarget))
        {
            await Store.UpdateJobAsync(jobId, JobStatus.Indexing, 94, "indexing", parsed.PageCount, parsed.PageCount, chunks.Length, documentId: documentId);
            var split = request.IndexTarget.Split(':', 2);
            await SaveDatasetAsync(documentId, split[0], split.Length > 1 ? split[1] : null);
        }
        var status = finalDocument.Status == "partial" ? JobStatus.Partial : JobStatus.Completed;
        await Store.UpdateJobAsync(jobId, status, 100, finalDocument.Status, parsed.PageCount, parsed.PageCount, chunks.Length, documentId: documentId);
        await Store.AddEventAsync(jobId, "info", finalDocument.Status, $"Created {chunks.Length} chunks from {parsed.PageCount} pages.");
    }

    private async Task ApplyEmbeddingsAsync(RagChunk[] chunks, CancellationToken cancellationToken, bool save = true)
    {
        var vectors = await Embeddings.EmbedAsync(chunks.Select(c => c.EmbeddingText).ToArray(), cancellationToken);
        for (var index = 0; index < chunks.Length; index++) chunks[index].Embedding = vectors[index];
        if (save) await Store.SaveEmbeddingsAsync(chunks);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
    private static string StableId(string prefix, string value) => prefix + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return -1;
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
        return aa == 0 || bb == 0 ? 0 : dot / Math.Sqrt(aa * bb);
    }
    private static string Normalize(string text) => string.Join(' ', text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCommandAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var start = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start command: {fileName}");
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token); var stderr = process.StandardError.ReadToEndAsync(cts.Token);
        try { await process.WaitForExitAsync(cts.Token); } catch { try { process.Kill(true); } catch { } throw; }
        return (process.ExitCode, await stdout, await stderr);
    }

    public void Dispose()
    {
        _shutdown.Cancel(); _queue.Writer.TryComplete();
        foreach (var cancellation in _cancellations.Values) cancellation.Cancel();
        try { Task.WaitAll(_workers.ToArray(), TimeSpan.FromSeconds(5)); } catch { }
        _shutdown.Dispose();
    }
}

public static class PageRanges
{
    public static int[] Parse(string ranges, int maxPage)
    {
        var pages = new SortedSet<int>();
        foreach (var part in ranges.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-', 2, StringSplitOptions.TrimEntries);
            if (!int.TryParse(bounds[0], out var start)) throw new ArgumentException($"Invalid page range: {part}");
            var end = bounds.Length == 2 && int.TryParse(bounds[1], out var parsedEnd) ? parsedEnd : start;
            if (start < 1 || end < start || end > maxPage) throw new ArgumentOutOfRangeException(nameof(ranges), $"Page range must be between 1 and {maxPage}: {part}");
            foreach (var page in Enumerable.Range(start, end - start + 1)) pages.Add(page);
        }
        return pages.ToArray();
    }
}
