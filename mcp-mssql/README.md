# mcp-mssql

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Microsoft SQL Server over Streamable HTTP.

## Lineage

- Upstream / reference behavior: `little-fort/mcp-dotnet-mssql`.
- Strategy: C# implementation using `Microsoft.Data.SqlClient`.
- Runtime requirement: a SQL Server connection string must be supplied by `MSSQL_CONNECTION_STRING` or per tool call.
- Trusted-local Docker defaults allow non-query SQL execution, but the database connection is still explicit.

## Build

```powershell
docker build -t local/mcp-mssql .
```

## Run

```powershell
docker run --rm -p 8085:8080 -e MSSQL_CONNECTION_STRING="Server=host.docker.internal;Database=master;User Id=sa;Password=yourStrong(!)Password;TrustServerCertificate=True" local/mcp-mssql
```

Connect MCP clients with Streamable HTTP at `http://localhost:8085/mcp` or legacy SSE at `http://localhost:8085/sse`. Trusted-local images enable non-query SQL writes by default, but SQL tools still require a valid `MSSQL_CONNECTION_STRING` or per-call connection string.

## Tools

| Tool | What it does |
| --- | --- |
| `list_databases` | Lists databases visible to the configured login. |
| `list_schemas` | Lists schemas in the current database. |
| `list_tables` | Lists tables, optionally filtered by schema. |
| `describe_table` | Returns column metadata for a table. |
| `execute_read_query` | Executes read-only `SELECT` or `WITH` SQL and returns rows. |
| `execute_non_query` | Executes non-query SQL such as DDL/DML commands. |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MSSQL_CONNECTION_STRING` | empty | Default SQL Server connection string. |
| `MCP_ENABLE_SQL_WRITES` | `true` in Dockerfile | Optional compatibility switch; set `false` to block `execute_non_query`. |

## Notes

`execute_read_query` intentionally accepts only queries beginning with `SELECT` or `WITH`. Use `execute_non_query` for mutating SQL when running in a trusted environment.
