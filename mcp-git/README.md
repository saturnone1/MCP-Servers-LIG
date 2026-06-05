# mcp-git

C# remote MCP Git server over Streamable HTTP. It wraps the `git` CLI inside the container.

## Build

```powershell
docker build -t local/mcp-git .
```

## Run

```powershell
docker run --rm -p 8082:8080 -v ${PWD}:/workspace local/mcp-git
```

Connect MCP clients with Streamable HTTP at `http://localhost:8082/mcp` or legacy SSE at `http://localhost:8082/sse`. Trusted-local images enable mutating git tools by default and allow `/` inside the container unless `MCP_ALLOWED_DIRS` overrides it.
