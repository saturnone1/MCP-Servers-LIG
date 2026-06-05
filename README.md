# MCP Remote Server Bundle

This workspace contains six independent Docker-buildable remote MCP servers:

- `mcp-office`
- `mcp-filesystem`
- `mcp-git`
- `mcp-shell`
- `mcp-mssql`
- `mcp-dotnet`

Each server exposes Streamable HTTP at `/mcp`, legacy SSE at `/sse` with messages at `/message`, listens on container port `8080`, and exposes `/healthz` for health checks. These images are trusted-local defaults: write/execute tools are enabled and allowed paths default to `/` inside the container.

## Build All

```powershell
$servers = 'mcp-office','mcp-filesystem','mcp-git','mcp-shell','mcp-mssql','mcp-dotnet'
foreach ($server in $servers) {
  docker build -t "local/$server" $server
}
```

Runtime images are designed to run without internet access. Build-time package restore and upstream downloads are allowed.
