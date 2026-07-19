using ModelContextProtocol.Server;
using System.ComponentModel;

public sealed class PdfTools
{
    [McpServerTool(ReadOnly = true), Description("Return MCP-PDF configuration, parser health, profiles, storage, and optional dependency status.")]
    public static Task<object> Config() => PdfRuntime.Current.ConfigAsync();

    [McpServerTool, Description("Register and asynchronously ingest a PDF into structured pages, elements, chunks, and optional embeddings/indexes.")]
    public static Task<object> StartPdfIngest(string source, string? profile = null, string? chunkProfile = null, bool force = false, bool generateEmbeddings = false, string? indexTarget = null) =>
        PdfRuntime.Current.StartIngestAsync(source, profile, chunkProfile, force, generateEmbeddings, indexTarget);

    [McpServerTool(ReadOnly = true), Description("Get the status, progress, page count, chunk count, and error for an ingest job.")]
    public static async Task<JobRecord> GetPdfJobStatus(string jobId) => await PdfRuntime.Current.Store.GetJobAsync(jobId) ?? throw new KeyNotFoundException($"Job not found: {jobId}");

    [McpServerTool(ReadOnly = true), Description("List recent PDF processing jobs, optionally filtered by status.")]
    public static Task<JobRecord[]> ListPdfJobs(int limit = 100, string? status = null) => PdfRuntime.Current.Store.ListJobsAsync(limit, status);

    [McpServerTool(ReadOnly = true), Description("List stage transitions, warnings, and errors recorded for a PDF processing job.")]
    public static Task<object[]> GetPdfJobEvents(string jobId, int limit = 500) => PdfRuntime.Current.Store.GetJobEventsAsync(jobId, limit);

    [McpServerTool, Description("Request cancellation of a queued or running PDF processing job.")]
    public static Task<object> CancelPdfJob(string jobId) => PdfRuntime.Current.CancelJobAsync(jobId);

    [McpServerTool, Description("Retry PDF processing. The safe implementation rebuilds the document while preserving its stable document ID and version history.")]
    public static async Task<object> RetryPdfPages(string documentId, string pages, string profile = "scanned-ko")
    {
        var document = await PdfRuntime.Current.Store.GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        _ = PageRanges.Parse(pages, document.PageCount);
        var result = await PdfRuntime.Current.StartIngestAsync(document.SourcePath, profile, null, true, false, null);
        return new { documentId, requestedPages = pages, mode = "full-document-safe-rebuild", job = result };
    }

    [McpServerTool(ReadOnly = true), Description("List managed PDF documents by title, filename, or source path.")]
    public static Task<DocumentRecord[]> ListPdfDocuments(string? query = null, int limit = 100) => PdfRuntime.Current.Store.ListDocumentsAsync(query, limit);

    [McpServerTool(ReadOnly = true), Description("Compare registered PDF source files with their stored hashes and report changed, missing, and unchanged documents.")]
    public static Task<object[]> CheckPdfChanges(string? documentId = null) => PdfRuntime.Current.CheckChangesAsync(documentId);

    [McpServerTool(ReadOnly = true), Description("Get metadata and processing state for one managed PDF document.")]
    public static async Task<DocumentRecord> GetPdfDocument(string documentId) => await PdfRuntime.Current.Store.GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");

    [McpServerTool, Description("Delete a managed PDF dataset, including pages, elements, chunks, artifacts, warnings, and local search indexes. The source PDF is not deleted.")]
    public static async Task<object> DeletePdfDocument(string documentId)
    {
        PdfRuntime.Current.Settings.RequireWrites();
        await PdfRuntime.Current.Store.DeleteDocumentAsync(documentId);
        return new { deleted = documentId, sourcePdfDeleted = false };
    }

    [McpServerTool(ReadOnly = true), Description("Return the inferred table of contents from heading elements with page numbers.")]
    public static async Task<object[]> GetPdfToc(string documentId)
    {
        var elements = await PdfRuntime.Current.Store.GetElementsAsync(documentId);
        return elements.Where(e => e.Type is "heading" or "title").Select(e => (object)new { title = e.Text, e.HeadingPath, page = e.PageStart, e.ElementId }).ToArray();
    }

    [McpServerTool(ReadOnly = true), Description("Read extracted text for a page range such as '1-5,8'.")]
    public static async Task<ParsedPage[]> ReadPdfPages(string documentId, string pages)
    {
        var document = await PdfRuntime.Current.Store.GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        var numbers = PageRanges.Parse(pages, document.PageCount);
        var rows = new List<ParsedPage>();
        foreach (var group in Consecutive(numbers)) rows.AddRange(await PdfRuntime.Current.Store.ReadPagesAsync(documentId, group.Start, group.End));
        return rows.ToArray();
    }

    [McpServerTool(ReadOnly = true), Description("Return structured table elements extracted from a PDF, optionally restricted to a page.")]
    public static async Task<object[]> GetPdfTables(string documentId, int? page = null)
    {
        var elements = await PdfRuntime.Current.Store.GetElementsAsync(documentId);
        return elements.Where(e => e.Type == "table" && (!page.HasValue || e.PageStart <= page && e.PageEnd >= page)).Select(e => (object)new { e.ElementId, e.Text, e.StructuredData, e.PageStart, e.PageEnd, e.Caption, e.HeadingPath }).ToArray();
    }

    [McpServerTool(ReadOnly = true), Description("List extracted PDF image artifacts and captions.")]
    public static Task<ParsedArtifact[]> GetPdfImages(string documentId) => PdfRuntime.Current.Store.ListArtifactsAsync(documentId, "image");

    [McpServerTool(ReadOnly = true), Description("Render selected PDF pages to PNG with an installed pdftoppm-compatible renderer.")]
    public static Task<object> RenderPdfPages(string documentId, string pages, int dpi = 144) => PdfRuntime.Current.RenderPagesAsync(documentId, pages, dpi);

    [McpServerTool(ReadOnly = true), Description("List chunks from a managed PDF with pagination and optional content-type filtering.")]
    public static Task<RagChunk[]> ListPdfChunks(string documentId, int offset = 0, int limit = 100, string? contentType = null) => PdfRuntime.Current.Store.ListChunksAsync(documentId, offset, limit, contentType);

    [McpServerTool(ReadOnly = true), Description("Get one PDF chunk with source pages, heading path, neighboring IDs, and processing metadata.")]
    public static async Task<RagChunk> GetPdfChunk(string chunkId) => await PdfRuntime.Current.Store.GetChunkAsync(chunkId) ?? throw new KeyNotFoundException($"Chunk not found: {chunkId}");

    [McpServerTool, Description("Update a managed chunk's text and embedding text. Existing embedding is invalidated.")]
    public static async Task<object> UpdatePdfChunk(string chunkId, string text, string? embeddingText = null)
    {
        PdfRuntime.Current.Settings.RequireWrites();
        await PdfRuntime.Current.Store.UpdateChunkTextAsync(chunkId, text, embeddingText ?? text, TokenCounter.Count(embeddingText ?? text));
        return new { updated = chunkId, embeddingInvalidated = true };
    }

    [McpServerTool, Description("Delete one managed chunk from SQLite and its keyword-search index.")]
    public static async Task<object> DeletePdfChunk(string chunkId)
    {
        PdfRuntime.Current.Settings.RequireWrites();
        await PdfRuntime.Current.Store.DeleteChunkAsync(chunkId);
        return new { deleted = chunkId };
    }

    [McpServerTool, Description("Rebuild chunks from stored parsed elements without reparsing the PDF.")]
    public static Task<object> RechunkPdf(string documentId, string chunkProfile = "rag-default") => PdfRuntime.Current.RechunkAsync(documentId, chunkProfile);

    [McpServerTool(ReadOnly = true), Description("Search managed PDF chunks using keyword, vector, or hybrid mode. This returns evidence; it does not generate an answer.")]
    public static Task<SearchResult[]> SearchPdfContent(string query, string? documentId = null, string mode = "hybrid", int limit = 10) => mode.ToLowerInvariant() switch
    {
        "keyword" => PdfRuntime.Current.Store.KeywordSearchAsync(query, documentId, limit),
        "vector" => PdfRuntime.Current.VectorSearchAsync(query, documentId, limit),
        "hybrid" => PdfRuntime.Current.HybridSearchAsync(query, documentId, limit),
        _ => throw new ArgumentException("mode must be keyword, vector, or hybrid")
    };

    [McpServerTool(ReadOnly = true), Description("Find source chunks suitable as evidence for an LLM response. No RAG server or LLM is called.")]
    public static Task<SearchResult[]> FindPdfSources(string query, string? documentId = null, int limit = 8) => PdfRuntime.Current.HybridSearchAsync(query, documentId, limit);

    [McpServerTool(ReadOnly = true), Description("Validate page coverage, chunk sizes, duplicates, OCR usage, and processing warnings.")]
    public static Task<ValidationReport> ValidatePdfDataset(string documentId) => PdfRuntime.Current.ValidateAsync(documentId);

    [McpServerTool(ReadOnly = true), Description("List parser, OCR, empty-page, and other processing warnings for a document.")]
    public static Task<object[]> GetPdfProcessingWarnings(string documentId) => PdfRuntime.Current.Store.GetWarningsAsync(documentId);

    [McpServerTool, Description("Generate or regenerate embeddings for every chunk in a managed PDF using an OpenAI-compatible embedding endpoint.")]
    public static Task<object> GeneratePdfEmbeddings(string documentId) => PdfRuntime.Current.GenerateEmbeddingsAsync(documentId);

    [McpServerTool, Description("Store chunks in SQLite, PostgreSQL, or Qdrant. Qdrant requires embeddings.")]
    public static Task<object> SavePdfDataset(string documentId, string provider = "sqlite", string? target = null) => PdfRuntime.Current.SaveDatasetAsync(documentId, provider, target);

    [McpServerTool(ReadOnly = true), Description("List SQLite, PostgreSQL, and Qdrant dataset write operations with success or failure details.")]
    public static Task<object[]> ListPdfStorageOperations(string? documentId = null, int limit = 100) => PdfRuntime.Current.Store.ListIndexOperationsAsync(documentId, limit);

    [McpServerTool, Description("Export all chunks for a document to JSONL or Parquet.")]
    public static Task<object> ExportPdfDataset(string documentId, string format = "jsonl", string? destination = null) => PdfRuntime.Current.ExportAsync(documentId, format, destination);

    private static IEnumerable<(int Start, int End)> Consecutive(int[] pages)
    {
        if (pages.Length == 0) yield break;
        var start = pages[0]; var previous = start;
        foreach (var page in pages.Skip(1))
        {
            if (page != previous + 1) { yield return (start, previous); start = page; }
            previous = page;
        }
        yield return (start, previous);
    }
}
