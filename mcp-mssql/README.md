# mcp-mssql

C# remote MCP server for Microsoft SQL Server over Streamable HTTP.

## Build

```powershell
docker build -t local/mcp-mssql .
```

## Run

```powershell
docker run --rm -p 8085:8080 -e MSSQL_CONNECTION_STRING="Server=host.docker.internal;Database=master;User Id=sa;Password=yourStrong(!)Password;TrustServerCertificate=True" local/mcp-mssql
```

Connect MCP clients with Streamable HTTP at `http://localhost:8085/mcp` or legacy SSE at `http://localhost:8085/sse`. Trusted-local images enable non-query SQL writes by default, but SQL tools still require a valid `MSSQL_CONNECTION_STRING` or per-call connection string.
