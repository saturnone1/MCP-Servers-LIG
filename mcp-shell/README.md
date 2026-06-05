# mcp-shell

C# remote MCP shell server over Streamable HTTP.

## Build

```powershell
docker build -t local/mcp-shell .
```

## Run

```powershell
docker run --rm -p 8083:8080 -v ${PWD}:/workspace local/mcp-shell
```

Connect MCP clients with Streamable HTTP at `http://localhost:8083/mcp` or legacy SSE at `http://localhost:8083/sse`. Trusted-local images enable shell execution by default. Use `MCP_SHELL_ALLOWED_COMMANDS` and `MCP_SHELL_ALLOWED_ENV` for optional allowlists.
