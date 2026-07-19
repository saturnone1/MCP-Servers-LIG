# MCP-PDF

MCP-PDF is the suite's PDF ingestion, dataset-management, and evidence-reading server on port `42199`. It converts PDFs through Docling, preserves page and structural provenance, builds RAG-ready chunks, and lets an MCP client inspect the resulting PDF information directly.

It deliberately does **not** call a RAG server, assemble prompts, rerank for a separate RAG application, invoke an LLM, or generate final answers. A future RAG server can consume the datasets exported or stored by MCP-PDF.

## Capabilities

- PDF registration, SHA-256 deduplication, source-change detection, and version metadata
- asynchronous queued jobs with cancellation, recovery, progress, event, warning, and error records
- Docling Serve (`remote`) and installed Docling CLI (`local`) adapters
- text, forced-OCR metadata, tables, headings, pictures, pages, captions, and bounding-box provenance
- heading/table-aware token chunking with deterministic IDs, neighboring context, and source metadata
- SQLite persistence and Korean/CJK-friendly keyword search
- optional OpenAI-compatible embeddings, local vector/hybrid evidence search, PostgreSQL export, and Qdrant upsert
- chunk reading, editing, deletion, rechunking, validation, JSONL export, and Parquet export
- direct PDF information access through TOC, page, table, image, rendered-page, chunk, and evidence tools

## Docling

The default mode expects Docling Serve at `http://127.0.0.1:5001`. Large conversions use `/v1/convert/file/async`, poll task state, and fetch `/v1/result`; older services that return 404/405 automatically fall back to synchronous `/v1/convert/file`. Set `DOCLING_SERVICE_URL` and optionally `DOCLING_SERVICE_API_KEY` for another service. For a locally installed Docling CLI, set `DOCLING_MODE=local` and `DOCLING_COMMAND`.

Docling itself is an external processing dependency and is not embedded in the Windows installer. MCP-PDF still starts without it; `config` reports parser availability and ingest jobs record a clear failure until Docling is reachable. Page rendering similarly requires a `pdftoppm`-compatible executable only when `render_pdf_pages` is used.

## Storage and profiles

The default data root is `%LOCALAPPDATA%\LIG AI MCP\pdf`. SQLite stores operational state and current/versioned datasets. Parser profiles are `fast`, `balanced-ko`, `accurate-ko`, and `scanned-ko`; chunk profiles are `rag-default`, `rag-small`, and `rag-large`. Copy and edit `config/profiles.json` to customize them.

See `config/pdf.env.example` for every supported environment variable. `MCP_ALLOWED_DIRS=*` discovers every ready drive, matching the rest of the Windows suite.

## Development and tests

```powershell
.\mcp-pdf\scripts\run-dev.ps1
dotnet run --project .\mcp-pdf\tests\McpPdf.Tests.csproj -c Release
.\tests\pdf-smoke.ps1
```

The first test covers normalization, tables, structural chunks, SQLite transactions, Korean substring fallback, version isolation, JSONL, and Parquet. The smoke test uses a mock Docling Serve instance and exercises the real Streamable HTTP MCP path from ingest through evidence reading and export.
