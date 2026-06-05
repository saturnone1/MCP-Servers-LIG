# mcp-hwp

C# remote MCP server for Korean Hangul Word Processor files over Streamable HTTP and legacy SSE.

## Build

```powershell
docker build -t local/mcp-hwp .
```

## Run

```powershell
docker run --rm -p 8086:8080 -v ${PWD}:/workspace local/mcp-hwp
```

Connect MCP clients with Streamable HTTP at `http://localhost:8086/mcp` or legacy SSE at `http://localhost:8086/sse`.

Tools:

- `extract_text`: extracts readable text from `.hwp` and `.hwpx`.
- `inspect`: returns file metadata.
- `convert`: converts to `txt`, `docx`, `pdf`, or `odt`.

`.hwp` uses `pyhwp`/`hwp5txt` first, with LibreOffice headless conversion as a fallback. `.hwpx` is parsed directly as ZIP/XML.
