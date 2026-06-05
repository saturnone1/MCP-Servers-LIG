# mcp-dotnet

C# remote MCP server for the .NET SDK over Streamable HTTP.

## Build

```powershell
docker build -t local/mcp-dotnet .
```

## Run

```powershell
docker run --rm -p 8084:8080 -v ${PWD}:/workspace local/mcp-dotnet
```

Connect MCP clients with Streamable HTTP at `http://localhost:8084/mcp` or legacy SSE at `http://localhost:8084/sse`. Trusted-local images enable mutating operations such as `add package` and `format` by default.
