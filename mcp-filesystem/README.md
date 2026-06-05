# mcp-filesystem

C# remote MCP filesystem server over Streamable HTTP.

## Build

```powershell
docker build -t local/mcp-filesystem .
```

## Run

```powershell
docker run --rm -p 8081:8080 -v ${PWD}:/workspace local/mcp-filesystem
```

Connect MCP clients with Streamable HTTP at `http://localhost:8081/mcp` or legacy SSE at `http://localhost:8081/sse`. Trusted-local images enable writes by default and allow `/` inside the container unless `MCP_ALLOWED_DIRS` overrides it.
