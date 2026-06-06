# mcp-hwp

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Korean Hangul Word Processor files over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: none.
- Strategy: local C# MCP server using open tools for HWP/HWPX handling.
- `.hwp`: extracted with `pyhwp`/`hwp5txt` first, then LibreOffice headless conversion as a fallback.
- `.hwpx`: parsed directly as ZIP/XML, reading text nodes from document XML.
- Conversion: `txt` output is written through the text extractor. `docx`, `pdf`, and `odt` output is delegated to LibreOffice and is reported as an error if LibreOffice does not create an output file.

## Build

```powershell
docker build -t local/mcp-hwp .
```

## Run

```powershell
docker run --rm -p 8086:8080 -v ${PWD}:/workspace local/mcp-hwp
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
| `extract_text` | `path` string, `maxChars` int = `20000` | Extracted text. If the mapped file is missing, returns a file-not-found message. |
| `inspect` | `path` string | Metadata object. Missing files return `{ "exists": false, "requestedPath": ..., "mappedPath": ..., "error": ... }`. |
| `convert` | `path` string, `outputDirectory` string = `/tmp/hwp-output`, `format` string = `txt`, `timeoutMs` int = `120000` | `{ "exitCode": number, "stdout": string, "stderr": string }`. `txt` writes extracted text. `docx`, `pdf`, and `odt` use LibreOffice. |

Supported formats are `txt`, `docx`, `pdf`, and `odt`.

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots for file paths. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `HWP5TXT_PATH` | `/opt/pyhwp/bin/hwp5txt` | Override `hwp5txt` executable path. |
| `SOFFICE_PATH` | `/usr/bin/soffice` | Override LibreOffice executable path. |

## Notes

`.hwp` uses `pyhwp`/`hwp5txt` first, with LibreOffice headless conversion as a fallback. `.hwpx` is parsed directly as ZIP/XML. `convert` writes `txt` output from extracted text. `docx`, `pdf`, and `odt` conversion fails loudly when LibreOffice cannot create an output file.
