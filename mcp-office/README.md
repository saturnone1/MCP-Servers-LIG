# mcp-office

C# remote MCP wrapper for OfficeCLI over Streamable HTTP.

## Build

```powershell
docker build -t local/mcp-office .
```

The Dockerfile downloads the Linux x64 OfficeCLI release during build and embeds it in the final image, so runtime does not need internet access.

## Run

```powershell
docker run --rm -p 8080:8080 -v ${PWD}:/workspace local/mcp-office
```

Connect MCP clients with Streamable HTTP at `http://localhost:8080/mcp` or legacy SSE at `http://localhost:8080/sse`. Trusted-local images enable document creation, batch edits, render/export, and raw OfficeCLI calls by default.
