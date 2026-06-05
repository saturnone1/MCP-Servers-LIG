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

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots for file paths. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `HWP5TXT_PATH` | `/opt/pyhwp/bin/hwp5txt` | Override `hwp5txt` executable path. |
| `SOFFICE_PATH` | `/usr/bin/soffice` | Override LibreOffice executable path. |

## Notes

`.hwp` uses `pyhwp`/`hwp5txt` first, with LibreOffice headless conversion as a fallback. `.hwpx` is parsed directly as ZIP/XML. `convert` writes `txt` output from extracted text. `docx`, `pdf`, and `odt` conversion fails loudly when LibreOffice cannot create an output file.
