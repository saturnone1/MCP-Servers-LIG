# mcp-hwp

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Korean Hangul Word Processor files over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: none.
- Strategy: local C# MCP server using open tools for HWP/HWPX handling.
- `.hwp`: extracted first with a built-in C# OLE/BodyText parser, then optional `hwp5txt`, then LibreOffice headless conversion as fallback.
- `.hwpx`: parsed directly as ZIP/XML, reading text nodes from document XML.
- Conversion: `txt` output is written through the text extractor. `docx`, `pdf`, and `odt` output is delegated to LibreOffice and is reported as an error if LibreOffice does not create an output file.

## Build

```powershell
docker build -t local/mcp-hwp .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-hwp:latest` as `airgap/local-mcp-hwp.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-hwp -Port 8086
```

Connect MCP clients with Streamable HTTP at `http://localhost:8086/mcp` or legacy SSE at `http://localhost:8086/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `extract_text` | Extracts readable text from `.hwp` and `.hwpx`. |
| `inspect` | Returns basic file metadata. |
| `convert` | Converts `.hwp` or `.hwpx` to `txt`, `docx`, `pdf`, or `odt`. Text output uses the extractor; other formats use LibreOffice. |

## API Reference

| Tool | Arguments | Returns |
| --- | --- | --- |
| `extract_text` | `path` string, `maxChars` int = `1000000` | Extracted text, up to 10,000,000 characters. |
| `inspect` | `path` string | Metadata object. Missing files return `{ "exists": false, "requestedPath": ..., "mappedPath": ..., "error": ... }`. |
| `convert` | `path` string, `outputDirectory` string = `/tmp/hwp-output`, `format` string = `txt`, `timeoutMs` int = `600000` | Up to 24 hours and 64 MiB output. `txt` writes extracted text; other formats use LibreOffice. |

Supported formats are `txt`, `docx`, `pdf`, and `odt`.

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots for file paths. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `MCP_ENABLE_HWP_WRITES` | `true` | Set `false` to block `convert`. |
| `HWP5TXT_PATH` | `hwp5txt` | Override optional fallback `hwp5txt` executable path. |
| `SOFFICE_PATH` | `soffice` | Override optional LibreOffice executable path. |

## Notes

`.hwp` uses the built-in C# parser first. If it cannot find text, it falls back to `hwp5txt`, then LibreOffice. `.hwpx` is parsed directly as ZIP/XML. `convert` writes `txt` output from extracted text. `docx`, `pdf`, and `odt` conversion fails loudly when LibreOffice cannot create an output file.

## Kubernetes

No Kubernetes manifests are provided for `mcp-hwp` in this phase. It is excluded because the workflow depends on document conversion tooling, fonts, and host document access patterns that need a separate cluster storage and rendering review.
