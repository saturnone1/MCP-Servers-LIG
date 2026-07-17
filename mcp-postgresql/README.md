# mcp-postgresql

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for PostgreSQL operations over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: none.
- Strategy: direct C# MCP server using `Npgsql`.
- Runtime requirement: `POSTGRES_CONNECTION_STRING` must point to a reachable PostgreSQL server.
- Trusted-local Docker defaults enable non-query SQL tools unless `MCP_ENABLE_POSTGRES_WRITES=false`.

## Build

```powershell
docker build -t local/mcp-postgresql .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-postgresql:latest` as `airgap/local-mcp-postgresql.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 127.0.0.1:8090:8080 `
  -e "POSTGRES_CONNECTION_STRING=Host=host.docker.internal;Database=postgres;Username=postgres;Password=postgres" `
  local/mcp-postgresql
```

Connect MCP clients with Streamable HTTP at `http://localhost:8090/mcp` or legacy SSE at `http://localhost:8090/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `config` | Returns whether a PostgreSQL connection string is configured. |
| `list_databases` | Lists databases visible to the configured login. |
| `list_schemas` | Lists schemas in the current database. |
| `list_tables` | Lists tables, optionally filtered by schema. |
| `describe_table` | Returns column metadata for a table. |
| `execute_read_query` | Executes read-only SQL and returns rows. |
| `execute_non_query` | Executes non-query SQL such as DDL/DML commands. |

## API Reference

| Tool | Arguments | Returns |
| --- | --- | --- |
| `config` | none | Connection configuration status. |
| `list_databases` | `connectionString` string? = `null` | Database metadata array. |
| `list_schemas` | `connectionString` string? = `null` | Schema metadata array. |
| `list_tables` | `connectionString` string? = `null`, `schema` string? = `null` | Table metadata array. |
| `describe_table` | `tableName` string, `schema` string = `public`, `connectionString` string? = `null` | Column metadata array. |
| `execute_read_query` | `sql` string, `connectionString` string? = `null`, `maxRows` int = `2000` | Up to 100,000 rows; read-query timeout is one hour. |
| `execute_non_query` | `sql` string, `connectionString` string? = `null`, `timeoutSeconds` int = `30` | Rows affected and execution metadata. |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `POSTGRES_CONNECTION_STRING` | empty | Default PostgreSQL connection string. |
| `MCP_ENABLE_POSTGRES_WRITES` | `true` in Dockerfile | Optional compatibility switch; set `false` to block `execute_non_query`. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The server is cluster-native when the PostgreSQL endpoint is reachable from the `mcp-servers` namespace and the connection string is provided as a Secret.

