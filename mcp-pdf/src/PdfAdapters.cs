using Npgsql;
using Parquet.Serialization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

public interface IEmbeddingProvider
{
    bool Available { get; }
    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

public sealed class OpenAiCompatibleEmbeddingProvider : IEmbeddingProvider
{
    private readonly PdfSettings _settings;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromHours(1) };
    public bool Available => _settings.EmbeddingProvider is not "none" && !string.IsNullOrWhiteSpace(_settings.EmbeddingEndpoint);

    public OpenAiCompatibleEmbeddingProvider(PdfSettings settings)
    {
        _settings = settings;
        if (!string.IsNullOrWhiteSpace(settings.EmbeddingApiKey))
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.EmbeddingApiKey);
    }

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (!Available) throw new InvalidOperationException("Set PDF_EMBEDDING_PROVIDER and PDF_EMBEDDING_ENDPOINT before generating embeddings.");
        var result = new List<float[]>();
        foreach (var batch in texts.Chunk(64))
        {
            using var response = await _client.PostAsJsonAsync(_settings.EmbeddingEndpoint, new { model = _settings.EmbeddingModel, input = batch }, JsonDefaults.Options, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Embedding endpoint returned HTTP {(int)response.StatusCode}: {body}");
            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Embedding response does not contain a data array.");
            result.AddRange(data.EnumerateArray().OrderBy(item => item.TryGetProperty("index", out var index) ? index.GetInt32() : 0)
                .Select(item => item.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray()));
        }
        if (result.Count != texts.Count) throw new InvalidDataException($"Embedding endpoint returned {result.Count} vectors for {texts.Count} inputs.");
        return result.ToArray();
    }
}

public sealed class DatasetExporter
{
    private static readonly JsonSerializerOptions JsonlOptions = new(JsonDefaults.Options) { WriteIndented = false };

    public async Task<object> ExportJsonlAsync(string path, IReadOnlyList<RagChunk> chunks, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JsonSerializer.Serialize(chunk, JsonlOptions));
        }
        await writer.FlushAsync(cancellationToken);
        await writer.DisposeAsync();
        await stream.DisposeAsync();
        return FileResult(path, chunks.Count);
    }

    public async Task<object> ExportParquetAsync(string path, IReadOnlyList<RagChunk> chunks, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rows = chunks.Select(chunk => new ParquetChunk
        {
            DocumentId = chunk.DocumentId, DocumentVersion = chunk.DocumentVersion, ChunkId = chunk.ChunkId, ChunkIndex = chunk.ChunkIndex,
            Text = chunk.Text, EmbeddingText = chunk.EmbeddingText, Title = chunk.Title, HeadingPath = string.Join(" > ", chunk.HeadingPath),
            ContentType = chunk.ContentType, PageStart = chunk.PageStart, PageEnd = chunk.PageEnd, TokenCount = chunk.TokenCount,
            Language = chunk.Language, OcrApplied = chunk.OcrApplied, Confidence = chunk.Confidence, SourcePath = chunk.SourcePath,
            SourceSha256 = chunk.SourceSha256, Parser = chunk.Parser, ParserVersion = chunk.ParserVersion, ParserProfile = chunk.ParserProfile,
            ChunkProfile = chunk.ChunkProfile, CreatedAt = chunk.CreatedAt.UtcDateTime
        }).ToArray();
        await ParquetSerializer.SerializeAsync(rows, path, cancellationToken: cancellationToken);
        return FileResult(path, chunks.Count);
    }

    private static object FileResult(string path, int records)
    {
        var file = new FileInfo(path);
        using var stream = File.OpenRead(path);
        return new { path = file.FullName, records, bytes = file.Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant() };
    }

    private sealed class ParquetChunk
    {
        public string DocumentId { get; set; } = "";
        public int DocumentVersion { get; set; }
        public string ChunkId { get; set; } = "";
        public int ChunkIndex { get; set; }
        public string Text { get; set; } = "";
        public string EmbeddingText { get; set; } = "";
        public string Title { get; set; } = "";
        public string HeadingPath { get; set; } = "";
        public string ContentType { get; set; } = "";
        public int PageStart { get; set; }
        public int PageEnd { get; set; }
        public int TokenCount { get; set; }
        public string Language { get; set; } = "";
        public bool OcrApplied { get; set; }
        public double? Confidence { get; set; }
        public string SourcePath { get; set; } = "";
        public string SourceSha256 { get; set; } = "";
        public string Parser { get; set; } = "";
        public string ParserVersion { get; set; } = "";
        public string ParserProfile { get; set; } = "";
        public string ChunkProfile { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}

public sealed class PostgreSqlDatasetWriter
{
    private readonly PdfSettings _settings;
    public PostgreSqlDatasetWriter(PdfSettings settings) => _settings = settings;
    public bool Available => !string.IsNullOrWhiteSpace(_settings.PostgreSqlConnectionString);

    public async Task<object> UpsertAsync(IReadOnlyList<RagChunk> chunks, CancellationToken cancellationToken)
    {
        if (!Available) throw new InvalidOperationException("Set PDF_POSTGRES_CONNECTION_STRING before exporting to PostgreSQL.");
        await using var connection = new NpgsqlConnection(_settings.PostgreSqlConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var schema = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS mcp_pdf_chunks(
              chunk_id text PRIMARY KEY, document_id text NOT NULL, document_version integer NOT NULL, chunk_index integer NOT NULL,
              text_content text NOT NULL, embedding_text text NOT NULL, title text NOT NULL, heading_path jsonb NOT NULL,
              content_type text NOT NULL, page_start integer NOT NULL, page_end integer NOT NULL, token_count integer NOT NULL,
              metadata jsonb NOT NULL, updated_at timestamptz NOT NULL DEFAULT now());
            CREATE INDEX IF NOT EXISTS ix_mcp_pdf_chunks_document ON mcp_pdf_chunks(document_id, document_version, chunk_index);
            CREATE INDEX IF NOT EXISTS ix_mcp_pdf_chunks_text ON mcp_pdf_chunks USING gin(to_tsvector('simple', text_content));
            """, connection)) await schema.ExecuteNonQueryAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var chunk in chunks)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO mcp_pdf_chunks(chunk_id,document_id,document_version,chunk_index,text_content,embedding_text,title,heading_path,content_type,page_start,page_end,token_count,metadata,updated_at)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8::jsonb,$9,$10,$11,$12,$13::jsonb,now())
                ON CONFLICT(chunk_id) DO UPDATE SET text_content=excluded.text_content,embedding_text=excluded.embedding_text,title=excluded.title,heading_path=excluded.heading_path,content_type=excluded.content_type,page_start=excluded.page_start,page_end=excluded.page_end,token_count=excluded.token_count,metadata=excluded.metadata,updated_at=now()
                """, connection, transaction);
            command.Parameters.AddWithValue(chunk.ChunkId); command.Parameters.AddWithValue(chunk.DocumentId); command.Parameters.AddWithValue(chunk.DocumentVersion); command.Parameters.AddWithValue(chunk.ChunkIndex);
            command.Parameters.AddWithValue(chunk.Text); command.Parameters.AddWithValue(chunk.EmbeddingText); command.Parameters.AddWithValue(chunk.Title); command.Parameters.AddWithValue(JsonSerializer.Serialize(chunk.HeadingPath));
            command.Parameters.AddWithValue(chunk.ContentType); command.Parameters.AddWithValue(chunk.PageStart); command.Parameters.AddWithValue(chunk.PageEnd); command.Parameters.AddWithValue(chunk.TokenCount); command.Parameters.AddWithValue(JsonSerializer.Serialize(chunk, JsonDefaults.Options));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new { provider = "postgresql", records = chunks.Count };
    }
}

public sealed class QdrantDatasetWriter
{
    private readonly PdfSettings _settings;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromHours(1) };
    public QdrantDatasetWriter(PdfSettings settings)
    {
        _settings = settings;
        if (!string.IsNullOrWhiteSpace(settings.QdrantApiKey)) _client.DefaultRequestHeaders.TryAddWithoutValidation("api-key", settings.QdrantApiKey);
    }

    public async Task<object> UpsertAsync(string collection, IReadOnlyList<RagChunk> chunks, CancellationToken cancellationToken)
    {
        var embedded = chunks.Where(c => c.Embedding is { Length: > 0 }).ToArray();
        if (embedded.Length == 0) throw new InvalidOperationException("Generate embeddings before writing to Qdrant.");
        var vectorSize = embedded[0].Embedding!.Length;
        using (var create = await _client.PutAsJsonAsync($"{_settings.QdrantUrl}/collections/{Uri.EscapeDataString(collection)}", new { vectors = new { size = vectorSize, distance = "Cosine" } }, JsonDefaults.Options, cancellationToken))
        {
            if (!create.IsSuccessStatusCode && create.StatusCode != System.Net.HttpStatusCode.Conflict)
            {
                var error = await create.Content.ReadAsStringAsync(cancellationToken);
                if (!error.Contains("already exists", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Qdrant collection creation failed: {error}");
            }
        }
        foreach (var batch in embedded.Chunk(100))
        {
            var points = batch.Select(c => new { id = GuidFromString(c.ChunkId), vector = c.Embedding, payload = new { c.ChunkId, c.DocumentId, c.DocumentVersion, c.ChunkIndex, c.Text, c.Title, c.HeadingPath, c.ContentType, c.PageStart, c.PageEnd, c.TokenCount, c.SourcePath } }).ToArray();
            using var response = await _client.PutAsJsonAsync($"{_settings.QdrantUrl}/collections/{Uri.EscapeDataString(collection)}/points?wait=true", new { points }, JsonDefaults.Options, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Qdrant upsert failed: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }
        return new { provider = "qdrant", collection, records = embedded.Length, vectorSize };
    }

    private static Guid GuidFromString(string value)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash);
    }
}
