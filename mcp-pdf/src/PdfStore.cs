using Microsoft.Data.Sqlite;
using System.Text.Json;

public sealed class PdfStore
{
    private readonly string _connectionString;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    static PdfStore() => SQLitePCL.Batteries_V2.Init();

    public PdfStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS schema_info(version INTEGER NOT NULL);
            INSERT INTO schema_info(version) SELECT 1 WHERE NOT EXISTS(SELECT 1 FROM schema_info);

            CREATE TABLE IF NOT EXISTS documents(
              document_id TEXT PRIMARY KEY,
              source_path TEXT NOT NULL,
              file_name TEXT NOT NULL,
              sha256 TEXT NOT NULL,
              file_size INTEGER NOT NULL,
              title TEXT NOT NULL,
              page_count INTEGER NOT NULL,
              current_version INTEGER NOT NULL,
              status TEXT NOT NULL,
              parser_profile TEXT NOT NULL,
              chunk_profile TEXT NOT NULL,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL,
              UNIQUE(sha256, parser_profile, chunk_profile)
            );
            CREATE INDEX IF NOT EXISTS ix_documents_path ON documents(source_path);
            CREATE INDEX IF NOT EXISTS ix_documents_hash ON documents(sha256);

            CREATE TABLE IF NOT EXISTS document_versions(
              document_id TEXT NOT NULL,
              version INTEGER NOT NULL,
              sha256 TEXT NOT NULL,
              parser TEXT NOT NULL,
              parser_version TEXT NOT NULL,
              parser_profile TEXT NOT NULL,
              chunk_profile TEXT NOT NULL,
              manifest_path TEXT,
              created_at TEXT NOT NULL,
              PRIMARY KEY(document_id, version),
              FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS jobs(
              job_id TEXT PRIMARY KEY,
              document_id TEXT,
              source_path TEXT NOT NULL,
              parser_profile TEXT NOT NULL,
              chunk_profile TEXT NOT NULL,
              status TEXT NOT NULL,
              progress REAL NOT NULL,
              current_stage TEXT NOT NULL,
              processed_pages INTEGER NOT NULL,
              total_pages INTEGER NOT NULL,
              chunks_created INTEGER NOT NULL,
              error TEXT,
              request_json TEXT NOT NULL,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_jobs_status ON jobs(status, created_at);

            CREATE TABLE IF NOT EXISTS job_events(
              event_id INTEGER PRIMARY KEY AUTOINCREMENT,
              job_id TEXT NOT NULL,
              level TEXT NOT NULL,
              stage TEXT NOT NULL,
              message TEXT NOT NULL,
              page_number INTEGER,
              created_at TEXT NOT NULL,
              FOREIGN KEY(job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS pages(
              document_id TEXT NOT NULL,
              version INTEGER NOT NULL,
              page_number INTEGER NOT NULL,
              text TEXT NOT NULL,
              ocr_applied INTEGER NOT NULL,
              confidence REAL,
              status TEXT NOT NULL,
              PRIMARY KEY(document_id, version, page_number),
              FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS elements(
              document_id TEXT NOT NULL,
              version INTEGER NOT NULL,
              element_id TEXT NOT NULL,
              type TEXT NOT NULL,
              text TEXT NOT NULL,
              heading_path_json TEXT NOT NULL,
              page_start INTEGER NOT NULL,
              page_end INTEGER NOT NULL,
              bbox_json TEXT,
              reading_order INTEGER NOT NULL,
              caption TEXT,
              confidence REAL,
              structured_data TEXT,
              PRIMARY KEY(document_id, version, element_id),
              FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS chunks(
              chunk_id TEXT PRIMARY KEY,
              document_id TEXT NOT NULL,
              version INTEGER NOT NULL,
              chunk_index INTEGER NOT NULL,
              text TEXT NOT NULL,
              embedding_text TEXT NOT NULL,
              title TEXT NOT NULL,
              heading_path_json TEXT NOT NULL,
              content_type TEXT NOT NULL,
              page_start INTEGER NOT NULL,
              page_end INTEGER NOT NULL,
              source_elements_json TEXT NOT NULL,
              token_count INTEGER NOT NULL,
              language TEXT NOT NULL,
              ocr_applied INTEGER NOT NULL,
              confidence REAL,
              previous_chunk_id TEXT,
              next_chunk_id TEXT,
              source_path TEXT NOT NULL,
              source_sha256 TEXT NOT NULL,
              parser TEXT NOT NULL,
              parser_version TEXT NOT NULL,
              parser_profile TEXT NOT NULL,
              chunk_profile TEXT NOT NULL,
              embedding_json TEXT,
              created_at TEXT NOT NULL,
              UNIQUE(document_id, version, chunk_index),
              FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_chunks_document ON chunks(document_id, version, chunk_index);
            CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(chunk_id UNINDEXED, document_id UNINDEXED, text, title, headings, tokenize='unicode61');

            CREATE TABLE IF NOT EXISTS artifacts(
              artifact_id TEXT PRIMARY KEY,
              document_id TEXT NOT NULL,
              version INTEGER NOT NULL,
              type TEXT NOT NULL,
              path TEXT NOT NULL,
              page_number INTEGER,
              caption TEXT,
              media_type TEXT,
              created_at TEXT NOT NULL,
              FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS warnings(
              warning_id INTEGER PRIMARY KEY AUTOINCREMENT,
              document_id TEXT NOT NULL,
              version INTEGER NOT NULL,
              code TEXT NOT NULL,
              message TEXT NOT NULL,
              page_number INTEGER,
              severity TEXT NOT NULL,
              created_at TEXT NOT NULL,
              FOREIGN KEY(document_id) REFERENCES documents(document_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS index_operations(
              operation_id TEXT PRIMARY KEY,
              document_id TEXT NOT NULL,
              version INTEGER NOT NULL,
              provider TEXT NOT NULL,
              target TEXT NOT NULL,
              status TEXT NOT NULL,
              records INTEGER NOT NULL,
              error TEXT,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
        await RecoverInterruptedJobsAsync(connection);
    }

    public async Task CreateJobAsync(JobRecord job, IngestRequest request)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO jobs(job_id,document_id,source_path,parser_profile,chunk_profile,status,progress,current_stage,processed_pages,total_pages,chunks_created,error,request_json,created_at,updated_at)
            VALUES($id,$document,$source,$parser,$chunk,$status,$progress,$stage,$processed,$total,$chunks,$error,$request,$created,$updated)
            """, ("$id", job.JobId), ("$document", job.DocumentId), ("$source", job.SourcePath), ("$parser", job.ParserProfile),
            ("$chunk", job.ChunkProfile), ("$status", job.Status.ToString()), ("$progress", job.Progress), ("$stage", job.CurrentStage),
            ("$processed", job.ProcessedPages), ("$total", job.TotalPages), ("$chunks", job.ChunksCreated), ("$error", job.Error),
            ("$request", JsonSerializer.Serialize(request, JsonOptions)), ("$created", Iso(job.CreatedAt)), ("$updated", Iso(job.UpdatedAt)));
        await AddEventAsync(job.JobId, "info", "queued", "Ingest job queued.");
    }

    public async Task UpdateJobAsync(string jobId, JobStatus status, double progress, string stage, int processedPages = 0, int totalPages = 0, int chunks = 0, string? error = null, string? documentId = null)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, """
            UPDATE jobs SET status=$status,progress=$progress,current_stage=$stage,processed_pages=$processed,total_pages=CASE WHEN $total=0 THEN total_pages ELSE $total END,
              chunks_created=CASE WHEN $chunks=0 THEN chunks_created ELSE $chunks END,error=$error,document_id=COALESCE($document,document_id),updated_at=$updated WHERE job_id=$id
            """, ("$status", status.ToString()), ("$progress", progress), ("$stage", stage), ("$processed", processedPages), ("$total", totalPages),
            ("$chunks", chunks), ("$error", error), ("$document", documentId), ("$updated", Iso(DateTimeOffset.UtcNow)), ("$id", jobId));
    }

    public async Task AddEventAsync(string jobId, string level, string stage, string message, int? pageNumber = null)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO job_events(job_id,level,stage,message,page_number,created_at) VALUES($job,$level,$stage,$message,$page,$created)",
            ("$job", jobId), ("$level", level), ("$stage", stage), ("$message", message), ("$page", pageNumber), ("$created", Iso(DateTimeOffset.UtcNow)));
    }

    public async Task<object[]> GetJobEventsAsync(string jobId, int limit = 500)
    {
        _ = await GetJobAsync(jobId) ?? throw new KeyNotFoundException($"Job not found: {jobId}");
        await using var connection = await OpenAsync();
        await using var command = Command(connection, "SELECT event_id,level,stage,message,page_number,created_at FROM job_events WHERE job_id=$id ORDER BY event_id LIMIT $limit", ("$id", jobId), ("$limit", Math.Clamp(limit, 1, 5000)));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object>();
        while (await reader.ReadAsync())
            rows.Add(new { eventId = reader.GetInt64(0), level = reader.GetString(1), stage = reader.GetString(2), message = reader.GetString(3), pageNumber = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4), createdAt = reader.GetString(5) });
        return rows.ToArray();
    }

    public async Task RecordIndexOperationAsync(string operationId, string documentId, int version, string provider, string target, string status, int records, string? error = null)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO index_operations(operation_id,document_id,version,provider,target,status,records,error,created_at,updated_at)
            VALUES($operation,$document,$version,$provider,$target,$status,$records,$error,$created,$updated)
            ON CONFLICT(operation_id) DO UPDATE SET status=excluded.status,records=excluded.records,error=excluded.error,updated_at=excluded.updated_at
            """, ("$operation", operationId), ("$document", documentId), ("$version", version), ("$provider", provider), ("$target", target), ("$status", status), ("$records", records), ("$error", error), ("$created", Iso(DateTimeOffset.UtcNow)), ("$updated", Iso(DateTimeOffset.UtcNow)));
    }

    public async Task<object[]> ListIndexOperationsAsync(string? documentId = null, int limit = 100)
    {
        await using var connection = await OpenAsync();
        var sql = "SELECT operation_id,document_id,version,provider,target,status,records,error,created_at,updated_at FROM index_operations" + (string.IsNullOrWhiteSpace(documentId) ? "" : " WHERE document_id=$document") + " ORDER BY created_at DESC LIMIT $limit";
        await using var command = Command(connection, sql, ("$document", documentId), ("$limit", Math.Clamp(limit, 1, 1000)));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object>();
        while (await reader.ReadAsync()) rows.Add(new { operationId = reader.GetString(0), documentId = reader.GetString(1), version = reader.GetInt32(2), provider = reader.GetString(3), target = reader.GetString(4), status = reader.GetString(5), records = reader.GetInt32(6), error = reader.IsDBNull(7) ? null : reader.GetString(7), createdAt = reader.GetString(8), updatedAt = reader.GetString(9) });
        return rows.ToArray();
    }

    public async Task<JobRecord?> GetJobAsync(string jobId)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, "SELECT job_id,document_id,source_path,parser_profile,chunk_profile,status,progress,current_stage,processed_pages,total_pages,chunks_created,error,created_at,updated_at FROM jobs WHERE job_id=$id", ("$id", jobId));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadJob(reader) : null;
    }

    public async Task<JobRecord[]> ListJobsAsync(int limit, string? status = null)
    {
        await using var connection = await OpenAsync();
        var sql = "SELECT job_id,document_id,source_path,parser_profile,chunk_profile,status,progress,current_stage,processed_pages,total_pages,chunks_created,error,created_at,updated_at FROM jobs" +
                  (string.IsNullOrWhiteSpace(status) ? "" : " WHERE status=$status") + " ORDER BY created_at DESC LIMIT $limit";
        await using var command = Command(connection, sql, ("$status", status), ("$limit", Math.Clamp(limit, 1, 100000)));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<JobRecord>();
        while (await reader.ReadAsync()) rows.Add(ReadJob(reader));
        return rows.ToArray();
    }

    public async Task<IngestRequest?> GetJobRequestAsync(string jobId)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, "SELECT request_json FROM jobs WHERE job_id=$id", ("$id", jobId));
        var json = await command.ExecuteScalarAsync() as string;
        return json is null ? null : JsonSerializer.Deserialize<IngestRequest>(json, JsonOptions);
    }

    public async Task<DocumentRecord?> FindExistingAsync(string sha256, string parserProfile, string chunkProfile)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, "SELECT document_id,source_path,file_name,sha256,file_size,title,page_count,current_version,status,created_at,updated_at FROM documents WHERE sha256=$hash AND parser_profile=$parser AND chunk_profile=$chunk", ("$hash", sha256), ("$parser", parserProfile), ("$chunk", chunkProfile));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadDocument(reader) : null;
    }

    public async Task<DocumentRecord?> FindBySourceAsync(string sourcePath, string parserProfile, string chunkProfile)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, "SELECT document_id,source_path,file_name,sha256,file_size,title,page_count,current_version,status,created_at,updated_at FROM documents WHERE source_path=$path AND parser_profile=$parser AND chunk_profile=$chunk ORDER BY updated_at DESC LIMIT 1", ("$path", sourcePath), ("$parser", parserProfile), ("$chunk", chunkProfile));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadDocument(reader) : null;
    }

    public async Task SaveDocumentAsync(DocumentRecord document, ParsedDocument parsed, string parserProfile, string chunkProfile, IReadOnlyList<RagChunk> chunks, string manifestPath)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, """
            INSERT INTO documents(document_id,source_path,file_name,sha256,file_size,title,page_count,current_version,status,parser_profile,chunk_profile,created_at,updated_at)
            VALUES($id,$path,$file,$hash,$size,$title,$pages,$version,$status,$parserProfile,$chunkProfile,$created,$updated)
            ON CONFLICT(document_id) DO UPDATE SET source_path=excluded.source_path,file_name=excluded.file_name,sha256=excluded.sha256,file_size=excluded.file_size,title=excluded.title,page_count=excluded.page_count,current_version=excluded.current_version,status=excluded.status,parser_profile=excluded.parser_profile,chunk_profile=excluded.chunk_profile,updated_at=excluded.updated_at
            """, ("$id", document.DocumentId), ("$path", document.SourcePath), ("$file", document.FileName), ("$hash", document.Sha256), ("$size", document.FileSize),
            ("$title", document.Title), ("$pages", document.PageCount), ("$version", document.CurrentVersion), ("$status", document.Status),
            ("$parserProfile", parserProfile), ("$chunkProfile", chunkProfile), ("$created", Iso(document.CreatedAt)), ("$updated", Iso(document.UpdatedAt)), transaction);
        await ExecuteAsync(connection, "INSERT OR REPLACE INTO document_versions(document_id,version,sha256,parser,parser_version,parser_profile,chunk_profile,manifest_path,created_at) VALUES($id,$version,$hash,$parser,$parserVersion,$parserProfile,$chunkProfile,$manifest,$created)",
            ("$id", document.DocumentId), ("$version", document.CurrentVersion), ("$hash", document.Sha256), ("$parser", parsed.Parser), ("$parserVersion", parsed.ParserVersion),
            ("$parserProfile", parserProfile), ("$chunkProfile", chunkProfile), ("$manifest", manifestPath), ("$created", Iso(DateTimeOffset.UtcNow)), transaction);
        await DeleteVersionDataAsync(connection, transaction, document.DocumentId, document.CurrentVersion);

        foreach (var page in parsed.Pages)
            await ExecuteAsync(connection, "INSERT INTO pages(document_id,version,page_number,text,ocr_applied,confidence,status) VALUES($id,$version,$page,$text,$ocr,$confidence,$status)",
                ("$id", document.DocumentId), ("$version", document.CurrentVersion), ("$page", page.PageNumber), ("$text", page.Text), ("$ocr", page.OcrApplied ? 1 : 0), ("$confidence", page.Confidence), ("$status", page.Status), transaction);
        foreach (var element in parsed.Elements)
            await ExecuteAsync(connection, "INSERT INTO elements(document_id,version,element_id,type,text,heading_path_json,page_start,page_end,bbox_json,reading_order,caption,confidence,structured_data) VALUES($id,$version,$element,$type,$text,$headings,$start,$end,$bbox,$order,$caption,$confidence,$structured)",
                ("$id", document.DocumentId), ("$version", document.CurrentVersion), ("$element", element.ElementId), ("$type", element.Type), ("$text", element.Text),
                ("$headings", JsonSerializer.Serialize(element.HeadingPath, JsonOptions)), ("$start", element.PageStart), ("$end", element.PageEnd),
                ("$bbox", element.BoundingBox is null ? null : JsonSerializer.Serialize(element.BoundingBox, JsonOptions)), ("$order", element.ReadingOrder),
                ("$caption", element.Caption), ("$confidence", element.Confidence), ("$structured", element.StructuredData), transaction);
        foreach (var chunk in chunks)
            await InsertChunkAsync(connection, transaction, chunk);
        foreach (var artifact in parsed.Artifacts)
            await ExecuteAsync(connection, "INSERT OR REPLACE INTO artifacts(artifact_id,document_id,version,type,path,page_number,caption,media_type,created_at) VALUES($artifact,$id,$version,$type,$path,$page,$caption,$media,$created)",
                ("$artifact", artifact.ArtifactId), ("$id", document.DocumentId), ("$version", document.CurrentVersion), ("$type", artifact.Type), ("$path", artifact.Path),
                ("$page", artifact.PageNumber), ("$caption", artifact.Caption), ("$media", artifact.MediaType), ("$created", Iso(DateTimeOffset.UtcNow)), transaction);
        foreach (var warning in parsed.Warnings)
            await ExecuteAsync(connection, "INSERT INTO warnings(document_id,version,code,message,page_number,severity,created_at) VALUES($id,$version,$code,$message,$page,$severity,$created)",
                ("$id", document.DocumentId), ("$version", document.CurrentVersion), ("$code", warning.Code), ("$message", warning.Message), ("$page", warning.PageNumber), ("$severity", warning.Severity), ("$created", Iso(DateTimeOffset.UtcNow)), transaction);
        await transaction.CommitAsync();
    }

    public async Task<DocumentRecord?> GetDocumentAsync(string documentId)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, "SELECT document_id,source_path,file_name,sha256,file_size,title,page_count,current_version,status,created_at,updated_at FROM documents WHERE document_id=$id", ("$id", documentId));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadDocument(reader) : null;
    }

    public async Task<DocumentRecord[]> ListDocumentsAsync(string? query, int limit)
    {
        await using var connection = await OpenAsync();
        var sql = "SELECT document_id,source_path,file_name,sha256,file_size,title,page_count,current_version,status,created_at,updated_at FROM documents" +
                  (string.IsNullOrWhiteSpace(query) ? "" : " WHERE title LIKE $query OR file_name LIKE $query OR source_path LIKE $query") + " ORDER BY updated_at DESC LIMIT $limit";
        await using var command = Command(connection, sql, ("$query", $"%{query}%"), ("$limit", Math.Clamp(limit, 1, 100000)));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<DocumentRecord>();
        while (await reader.ReadAsync()) rows.Add(ReadDocument(reader));
        return rows.ToArray();
    }

    public async Task<ParsedPage[]> ReadPagesAsync(string documentId, int start, int end)
    {
        var document = await GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        await using var connection = await OpenAsync();
        await using var command = Command(connection, "SELECT page_number,text,ocr_applied,confidence,status FROM pages WHERE document_id=$id AND version=$version AND page_number BETWEEN $start AND $end ORDER BY page_number", ("$id", documentId), ("$version", document.CurrentVersion), ("$start", start), ("$end", end));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ParsedPage>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2) != 0, reader.IsDBNull(3) ? null : reader.GetDouble(3), reader.GetString(4)));
        return rows.ToArray();
    }

    public async Task<RagChunk?> GetChunkAsync(string chunkId)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, ChunkSelect + " WHERE chunk_id=$id", ("$id", chunkId));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadChunk(reader) : null;
    }

    public async Task<RagChunk[]> ListChunksAsync(string documentId, int offset, int limit, string? contentType = null)
    {
        await using var connection = await OpenAsync();
        var sql = ChunkSelect + " WHERE document_id=$id AND version=(SELECT current_version FROM documents WHERE document_id=$id)" + (string.IsNullOrWhiteSpace(contentType) ? "" : " AND content_type=$type") + " ORDER BY chunk_index LIMIT $limit OFFSET $offset";
        await using var command = Command(connection, sql, ("$id", documentId), ("$type", contentType), ("$limit", Math.Clamp(limit, 1, 1000000)), ("$offset", Math.Max(0, offset)));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<RagChunk>();
        while (await reader.ReadAsync()) rows.Add(ReadChunk(reader));
        return rows.ToArray();
    }

    public async Task UpdateChunkTextAsync(string chunkId, string text, string embeddingText, int tokenCount)
    {
        _ = await GetChunkAsync(chunkId) ?? throw new KeyNotFoundException($"Chunk not found: {chunkId}");
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "UPDATE chunks SET text=$text,embedding_text=$embedding,token_count=$tokens,embedding_json=NULL WHERE chunk_id=$id", ("$text", text), ("$embedding", embeddingText), ("$tokens", tokenCount), ("$id", chunkId), transaction);
        await ExecuteAsync(connection, "DELETE FROM chunks_fts WHERE chunk_id=$id", ("$id", chunkId), transaction);
        await ExecuteAsync(connection, "INSERT INTO chunks_fts(chunk_id,document_id,text,title,headings) SELECT chunk_id,document_id,text,title,heading_path_json FROM chunks WHERE chunk_id=$id", ("$id", chunkId), transaction);
        await transaction.CommitAsync();
    }

    public async Task DeleteChunkAsync(string chunkId)
    {
        var chunk = await GetChunkAsync(chunkId) ?? throw new KeyNotFoundException($"Chunk not found: {chunkId}");
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "DELETE FROM chunks_fts WHERE chunk_id=$id", ("$id", chunkId), transaction);
        await ExecuteAsync(connection, "DELETE FROM chunks WHERE chunk_id=$id", ("$id", chunkId), transaction);
        if (chunk.PreviousChunkId is not null)
            await ExecuteAsync(connection, "UPDATE chunks SET next_chunk_id=$next WHERE chunk_id=$id", ("$next", chunk.NextChunkId), ("$id", chunk.PreviousChunkId), transaction);
        if (chunk.NextChunkId is not null)
            await ExecuteAsync(connection, "UPDATE chunks SET previous_chunk_id=$previous WHERE chunk_id=$id", ("$previous", chunk.PreviousChunkId), ("$id", chunk.NextChunkId), transaction);
        await transaction.CommitAsync();
    }

    public async Task<SearchResult[]> KeywordSearchAsync(string query, string? documentId, int limit)
    {
        await using var connection = await OpenAsync();
        limit = Math.Clamp(limit, 1, 100);
        var sql = """
            SELECT c.chunk_id,c.document_id,d.title,c.text,c.heading_path_json,c.page_start,c.page_end,c.content_type,bm25(chunks_fts) AS rank
            FROM chunks_fts JOIN chunks c ON c.chunk_id=chunks_fts.chunk_id JOIN documents d ON d.document_id=c.document_id
            WHERE chunks_fts MATCH $query AND c.version=d.current_version
            """ + (string.IsNullOrWhiteSpace(documentId) ? "" : " AND c.document_id=$document") + " ORDER BY rank LIMIT $limit";
        var rows = new List<SearchResult>();
        {
            await using var command = Command(connection, sql, ("$query", ToFtsQuery(query)), ("$document", documentId), ("$limit", limit));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var rank = reader.GetDouble(8);
                rows.Add(ReadSearchResult(reader, 1d / (1d + Math.Abs(rank))));
            }
        }
        // unicode61 does not split every Korean/CJK grammatical suffix. A substring fallback
        // keeps natural Korean queries useful while FTS remains the ranked fast path.
        if (rows.Count == 0 && !string.IsNullOrWhiteSpace(query))
        {
            var likeSql = """
                SELECT c.chunk_id,c.document_id,d.title,c.text,c.heading_path_json,c.page_start,c.page_end,c.content_type
                FROM chunks c JOIN documents d ON d.document_id=c.document_id
                WHERE c.version=d.current_version AND (c.text LIKE $like ESCAPE '\' OR c.title LIKE $like ESCAPE '\')
                """ + (string.IsNullOrWhiteSpace(documentId) ? "" : " AND c.document_id=$document") + " ORDER BY c.document_id,c.chunk_index LIMIT $limit";
            var escaped = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            await using var command = Command(connection, likeSql, ("$like", $"%{escaped}%"), ("$document", documentId), ("$limit", limit));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(ReadSearchResult(reader, 0.5));
        }
        return rows.ToArray();
    }

    private static SearchResult ReadSearchResult(SqliteDataReader reader, double score) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), JsonSerializer.Deserialize<string[]>(reader.GetString(4), JsonOptions) ?? [], reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7), score);

    public async Task SaveEmbeddingsAsync(IReadOnlyList<RagChunk> chunks)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var chunk in chunks.Where(c => c.Embedding is not null))
            await ExecuteAsync(connection, "UPDATE chunks SET embedding_json=$embedding WHERE chunk_id=$id", ("$embedding", JsonSerializer.Serialize(chunk.Embedding, JsonOptions)), ("$id", chunk.ChunkId), transaction);
        await transaction.CommitAsync();
    }

    public async Task<RagChunk[]> GetEmbeddedChunksAsync(string? documentId = null)
    {
        await using var connection = await OpenAsync();
        var sql = ChunkSelect + " c WHERE embedding_json IS NOT NULL AND version=(SELECT current_version FROM documents d WHERE d.document_id=c.document_id)" + (string.IsNullOrWhiteSpace(documentId) ? "" : " AND document_id=$id") + " ORDER BY document_id,chunk_index";
        await using var command = Command(connection, sql, ("$id", documentId));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<RagChunk>();
        while (await reader.ReadAsync()) rows.Add(ReadChunk(reader));
        return rows.ToArray();
    }

    public async Task<ParsedElement[]> GetElementsAsync(string documentId)
    {
        var document = await GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        await using var connection = await OpenAsync();
        await using var command = Command(connection, "SELECT element_id,type,text,heading_path_json,page_start,page_end,bbox_json,reading_order,caption,confidence,structured_data FROM elements WHERE document_id=$id AND version=$version ORDER BY page_start,reading_order", ("$id", documentId), ("$version", document.CurrentVersion));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ParsedElement>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), JsonSerializer.Deserialize<string[]>(reader.GetString(3), JsonOptions) ?? [], reader.GetInt32(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<double[]>(reader.GetString(6), JsonOptions), reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetDouble(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
        return rows.ToArray();
    }

    public async Task ReplaceChunksAsync(string documentId, IReadOnlyList<RagChunk> chunks)
    {
        var document = await GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "DELETE FROM chunks_fts WHERE document_id=$id", ("$id", documentId), transaction);
        await ExecuteAsync(connection, "DELETE FROM chunks WHERE document_id=$id AND version=$version", ("$id", documentId), ("$version", document.CurrentVersion), transaction);
        foreach (var chunk in chunks) await InsertChunkAsync(connection, transaction, chunk);
        await transaction.CommitAsync();
    }

    public async Task DeleteDocumentAsync(string documentId)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "DELETE FROM chunks_fts WHERE document_id=$id", ("$id", documentId), transaction);
        await ExecuteAsync(connection, "DELETE FROM documents WHERE document_id=$id", ("$id", documentId), transaction);
        await transaction.CommitAsync();
    }

    public async Task<object[]> GetWarningsAsync(string documentId)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, "SELECT code,message,page_number,severity,created_at FROM warnings WHERE document_id=$id ORDER BY warning_id", ("$id", documentId));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object>();
        while (await reader.ReadAsync()) rows.Add(new { code = reader.GetString(0), message = reader.GetString(1), pageNumber = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2), severity = reader.GetString(3), createdAt = reader.GetString(4) });
        return rows.ToArray();
    }

    public async Task<ParsedArtifact[]> ListArtifactsAsync(string documentId, string? type = null)
    {
        var document = await GetDocumentAsync(documentId) ?? throw new KeyNotFoundException($"Document not found: {documentId}");
        await using var connection = await OpenAsync();
        var sql = "SELECT artifact_id,type,path,page_number,caption,media_type FROM artifacts WHERE document_id=$id AND version=$version" + (string.IsNullOrWhiteSpace(type) ? "" : " AND type=$type") + " ORDER BY page_number,artifact_id";
        await using var command = Command(connection, sql, ("$id", documentId), ("$version", document.CurrentVersion), ("$type", type));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ParsedArtifact>();
        while (await reader.ReadAsync()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        return rows.ToArray();
    }

    private async Task InsertChunkAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, RagChunk chunk)
    {
        await ExecuteAsync(connection, """
            INSERT INTO chunks(chunk_id,document_id,version,chunk_index,text,embedding_text,title,heading_path_json,content_type,page_start,page_end,source_elements_json,token_count,language,ocr_applied,confidence,previous_chunk_id,next_chunk_id,source_path,source_sha256,parser,parser_version,parser_profile,chunk_profile,embedding_json,created_at)
            VALUES($chunk,$document,$version,$index,$text,$embeddingText,$title,$headings,$type,$start,$end,$elements,$tokens,$language,$ocr,$confidence,$previous,$next,$path,$hash,$parser,$parserVersion,$parserProfile,$chunkProfile,$embedding,$created)
            """, ("$chunk", chunk.ChunkId), ("$document", chunk.DocumentId), ("$version", chunk.DocumentVersion), ("$index", chunk.ChunkIndex),
            ("$text", chunk.Text), ("$embeddingText", chunk.EmbeddingText), ("$title", chunk.Title), ("$headings", JsonSerializer.Serialize(chunk.HeadingPath, JsonOptions)),
            ("$type", chunk.ContentType), ("$start", chunk.PageStart), ("$end", chunk.PageEnd), ("$elements", JsonSerializer.Serialize(chunk.SourceElements, JsonOptions)),
            ("$tokens", chunk.TokenCount), ("$language", chunk.Language), ("$ocr", chunk.OcrApplied ? 1 : 0), ("$confidence", chunk.Confidence),
            ("$previous", chunk.PreviousChunkId), ("$next", chunk.NextChunkId), ("$path", chunk.SourcePath), ("$hash", chunk.SourceSha256),
            ("$parser", chunk.Parser), ("$parserVersion", chunk.ParserVersion), ("$parserProfile", chunk.ParserProfile), ("$chunkProfile", chunk.ChunkProfile),
            ("$embedding", chunk.Embedding is null ? null : JsonSerializer.Serialize(chunk.Embedding, JsonOptions)), ("$created", Iso(chunk.CreatedAt)), transaction);
        await ExecuteAsync(connection, "INSERT INTO chunks_fts(chunk_id,document_id,text,title,headings) VALUES($chunk,$document,$text,$title,$headings)",
            ("$chunk", chunk.ChunkId), ("$document", chunk.DocumentId), ("$text", chunk.Text), ("$title", chunk.Title), ("$headings", string.Join(" ", chunk.HeadingPath)), transaction);
    }

    private static async Task DeleteVersionDataAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string documentId, int version)
    {
        await ExecuteAsync(connection, "DELETE FROM chunks_fts WHERE document_id=$id", ("$id", documentId), transaction);
        foreach (var table in new[] { "pages", "elements", "chunks", "artifacts", "warnings" })
            await ExecuteAsync(connection, $"DELETE FROM {table} WHERE document_id=$id AND version=$version", ("$id", documentId), ("$version", version), transaction);
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=30000;";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task RecoverInterruptedJobsAsync(SqliteConnection connection) =>
        await ExecuteAsync(connection, "UPDATE jobs SET status='Queued',current_stage='recovered',error=NULL,updated_at=$updated WHERE status NOT IN ('Completed','Partial','Failed','Canceled')", ("$updated", Iso(DateTimeOffset.UtcNow)));

    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            if (sql.Contains(name, StringComparison.Ordinal)) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params object[] arguments)
    {
        var transaction = arguments.LastOrDefault() as System.Data.Common.DbTransaction;
        var parameterCount = transaction is null ? arguments.Length : arguments.Length - 1;
        var parameters = new (string Name, object? Value)[parameterCount];
        for (var index = 0; index < parameterCount; index++)
        {
            if (arguments[index] is not System.Runtime.CompilerServices.ITuple { Length: 2 } tuple || tuple[0] is not string name)
                throw new ArgumentException($"SQL argument {index} must be a (name, value) tuple.", nameof(arguments));
            parameters[index] = (name, tuple[1]);
        }
        await using var command = Command(connection, sql, parameters);
        command.Transaction = (SqliteTransaction?)transaction;
        await command.ExecuteNonQueryAsync();
    }

    private static JobRecord ReadJob(SqliteDataReader r) => new(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), Enum.Parse<JobStatus>(r.GetString(5)), r.GetDouble(6), r.GetString(7), r.GetInt32(8), r.GetInt32(9), r.GetInt32(10), r.IsDBNull(11) ? null : r.GetString(11), DateTimeOffset.Parse(r.GetString(12)), DateTimeOffset.Parse(r.GetString(13)));
    private static DocumentRecord ReadDocument(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetString(5), r.GetInt32(6), r.GetInt32(7), r.GetString(8), DateTimeOffset.Parse(r.GetString(9)), DateTimeOffset.Parse(r.GetString(10)));

    private static RagChunk ReadChunk(SqliteDataReader r) => new()
    {
        SchemaVersion = "1.0", ChunkId = r.GetString(0), DocumentId = r.GetString(1), DocumentVersion = r.GetInt32(2), ChunkIndex = r.GetInt32(3), Text = r.GetString(4), EmbeddingText = r.GetString(5), Title = r.GetString(6),
        HeadingPath = JsonSerializer.Deserialize<string[]>(r.GetString(7), JsonOptions) ?? [], ContentType = r.GetString(8), PageStart = r.GetInt32(9), PageEnd = r.GetInt32(10),
        SourceElements = JsonSerializer.Deserialize<string[]>(r.GetString(11), JsonOptions) ?? [], TokenCount = r.GetInt32(12), Language = r.GetString(13), OcrApplied = r.GetInt32(14) != 0,
        Confidence = r.IsDBNull(15) ? null : r.GetDouble(15), PreviousChunkId = r.IsDBNull(16) ? null : r.GetString(16), NextChunkId = r.IsDBNull(17) ? null : r.GetString(17),
        SourcePath = r.GetString(18), SourceSha256 = r.GetString(19), Parser = r.GetString(20), ParserVersion = r.GetString(21), ParserProfile = r.GetString(22), ChunkProfile = r.GetString(23),
        Embedding = r.IsDBNull(24) ? null : JsonSerializer.Deserialize<float[]>(r.GetString(24), JsonOptions), CreatedAt = DateTimeOffset.Parse(r.GetString(25))
    };

    private const string ChunkSelect = "SELECT chunk_id,document_id,version,chunk_index,text,embedding_text,title,heading_path_json,content_type,page_start,page_end,source_elements_json,token_count,language,ocr_applied,confidence,previous_chunk_id,next_chunk_id,source_path,source_sha256,parser,parser_version,parser_profile,chunk_profile,embedding_json,created_at FROM chunks";
    private static string Iso(DateTimeOffset value) => value.ToUniversalTime().ToString("O");
    private static string ToFtsQuery(string query) => string.Join(" AND ", query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(term => $"\"{term.Replace("\"", "\"\"")}\""));
}
