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

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-mssql:latest` as `airgap/local-mcp-mssql.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

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

## API Reference

SQL tools use `MSSQL_CONNECTION_STRING` by default. Pass `connectionString` per call to override it.

| Tool | Arguments | Returns |
| --- | --- | --- |
| `list_databases` | `connectionString` string? = `null` | Database metadata array. |
| `list_schemas` | `connectionString` string? = `null` | Schema metadata array. |
| `list_tables` | `connectionString` string? = `null`, `schema` string? = `null` | Table metadata array. |
| `describe_table` | `tableName` string, `schema` string = `dbo`, `connectionString` string? = `null` | Column metadata array. |
| `execute_read_query` | `sql` string, `connectionString` string? = `null`, `maxRows` int = `200` | Row objects. Only `SELECT` and `WITH` queries are accepted. |
| `execute_non_query` | `sql` string, `connectionString` string? = `null`, `timeoutSeconds` int = `30` | Rows affected and execution metadata. |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MSSQL_CONNECTION_STRING` | empty | Default SQL Server connection string. |
| `MCP_ENABLE_SQL_WRITES` | `true` in Dockerfile | Optional compatibility switch; set `false` to block `execute_non_query`. |

## Notes

`execute_read_query` intentionally accepts only queries beginning with `SELECT` or `WITH`. Use `execute_non_query` for mutating SQL when running in a trusted environment.

## Kubernetes

No Kubernetes manifests are provided for `mcp-mssql` in this phase. It was explicitly excluded from the requested Kubernetes set; a production cluster deployment should separately decide how to provide SQL connection strings, secrets, network policy, and database egress.
