using System.Text.Json.Serialization;

public enum JobStatus
{
    Queued,
    Inspecting,
    Parsing,
    Normalizing,
    Chunking,
    Validating,
    Embedding,
    Indexing,
    Completed,
    Partial,
    Failed,
    CancelRequested,
    Canceled,
    Retrying
}

public sealed record PdfProfile(
    string Name,
    string OcrMode,
    string[] OcrLanguages,
    string TableMode,
    bool ExtractImages,
    bool ExtractTables,
    bool EnrichCode,
    bool EnrichFormulas);

public sealed record ChunkProfile(
    string Name,
    int TargetTokens,
    int MaxTokens,
    int MinTokens,
    bool MergePeers,
    bool PreserveHeadingBoundary,
    bool PreserveTableBoundary,
    int ContextBeforeTokens,
    int ContextAfterTokens);

public sealed record ParsedDocument(
    string Title,
    int PageCount,
    IReadOnlyList<ParsedElement> Elements,
    IReadOnlyList<ParsedPage> Pages,
    IReadOnlyList<ParsedArtifact> Artifacts,
    IReadOnlyList<ProcessingWarning> Warnings,
    string Parser,
    string ParserVersion,
    double ProcessingSeconds);

public sealed record ParsedPage(
    int PageNumber,
    string Text,
    bool OcrApplied,
    double? Confidence,
    string Status = "parsed");

public sealed record ParsedElement(
    string ElementId,
    string Type,
    string Text,
    string[] HeadingPath,
    int PageStart,
    int PageEnd,
    double[]? BoundingBox,
    int ReadingOrder,
    string? Caption,
    double? Confidence,
    string? StructuredData = null);

public sealed record ParsedArtifact(
    string ArtifactId,
    string Type,
    string Path,
    int? PageNumber,
    string? Caption,
    string? MediaType);

public sealed record ProcessingWarning(
    string Code,
    string Message,
    int? PageNumber = null,
    string Severity = "warning");

public sealed record RagChunk
{
    public required string SchemaVersion { get; init; }
    public required string DocumentId { get; init; }
    public required int DocumentVersion { get; init; }
    public required string ChunkId { get; init; }
    public required int ChunkIndex { get; init; }
    public required string Text { get; init; }
    public required string EmbeddingText { get; init; }
    public required string Title { get; init; }
    public required string[] HeadingPath { get; init; }
    public required string ContentType { get; init; }
    public required int PageStart { get; init; }
    public required int PageEnd { get; init; }
    public required string[] SourceElements { get; init; }
    public required int TokenCount { get; init; }
    public required string Language { get; init; }
    public required bool OcrApplied { get; init; }
    public double? Confidence { get; init; }
    public string? PreviousChunkId { get; set; }
    public string? NextChunkId { get; set; }
    public required string SourcePath { get; init; }
    public required string SourceSha256 { get; init; }
    public required string Parser { get; init; }
    public required string ParserVersion { get; init; }
    public required string ParserProfile { get; init; }
    public required string ChunkProfile { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public float[]? Embedding { get; set; }
}

public sealed record DocumentRecord(
    string DocumentId,
    string SourcePath,
    string FileName,
    string Sha256,
    long FileSize,
    string Title,
    int PageCount,
    int CurrentVersion,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobRecord(
    string JobId,
    string? DocumentId,
    string SourcePath,
    string ParserProfile,
    string ChunkProfile,
    JobStatus Status,
    double Progress,
    string CurrentStage,
    int ProcessedPages,
    int TotalPages,
    int ChunksCreated,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record IngestRequest(
    string Source,
    string Profile,
    string ChunkProfile,
    bool Force,
    bool GenerateEmbeddings,
    string? IndexTarget);

public sealed record SearchResult(
    string ChunkId,
    string DocumentId,
    string DocumentTitle,
    string Text,
    string[] HeadingPath,
    int PageStart,
    int PageEnd,
    string ContentType,
    double Score);

public sealed record ValidationReport(
    string DocumentId,
    int PageCount,
    int ChunkCount,
    int EmptyPages,
    int ShortChunks,
    int OversizedChunks,
    int OcrPages,
    int WarningCount,
    double DuplicateRatio,
    string[] Issues);

public sealed record DoclingResultEnvelope
{
    [JsonPropertyName("document")]
    public DoclingDocumentEnvelope? Document { get; init; }
    [JsonPropertyName("status")]
    public string Status { get; init; } = "failure";
    [JsonPropertyName("processing_time")]
    public double ProcessingTime { get; init; }
    [JsonPropertyName("errors")]
    public object[] Errors { get; init; } = [];
}

public sealed record DoclingDocumentEnvelope
{
    [JsonPropertyName("md_content")]
    public string Markdown { get; init; } = "";
    [JsonPropertyName("text_content")]
    public string Text { get; init; } = "";
    [JsonPropertyName("json_content")]
    public object? Json { get; init; }
}
