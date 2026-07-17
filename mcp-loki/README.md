# mcp-loki

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Loki log analysis over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: none.
- Strategy: direct C# MCP server calling the Loki HTTP API.
- Runtime requirement: `LOKI_BASE_URL` must point to a reachable Loki or Loki gateway endpoint.

## Build

```powershell
docker build -t local/mcp-loki .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-loki:latest` as `airgap/local-mcp-loki.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 127.0.0.1:8093:8080 `
  -e "LOKI_BASE_URL=http://host.docker.internal:3100" `
  local/mcp-loki
```

Connect MCP clients with Streamable HTTP at `http://localhost:8093/mcp` or legacy SSE at `http://localhost:8093/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `config` | Returns Loki base URL and auth configuration. |
| `ready` | Calls `/ready`. |
| `query` | Runs an instant LogQL query. |
| `query_range` | Runs a LogQL range query. |
| `recent_logs` | Fetches recent log lines for a stream selector. |
| `search_logs` | Searches recent logs by adding `|=` or `|~` filters. |
| `labels` | Lists Loki labels. |
| `label_values` | Lists values for a label. |
| `series` | Finds matching series. |
| `index_stats` | Returns index stats for a stream selector. |

## API Reference

Tools return `{ "statusCode": number, "success": bool, "body": string }`.

| Tool | Arguments |
| --- | --- |
| `config` | none |
| `ready` | none |
| `query` | `query` string, `time` string? = `null`, `limit` int = `100`, `direction` string = `backward` |
| `query_range` | `query` string, `start` string, `end` string, `step` string? = `null`, `limit` int = `100`, `direction` string = `backward` |
| `recent_logs` | `selector` string, `sinceMinutes` int = `30`, `limit` int = `200`, `direction` string = `backward` |
| `search_logs` | `selector` string, `pattern` string, `sinceMinutes` int = `30`, `limit` int = `200`, `regex` bool = `false`, `direction` string = `backward` |
| `labels` | `start` string? = `null`, `end` string? = `null` |
| `label_values` | `label` string, `start` string? = `null`, `end` string? = `null` |
| `series` | `match` string array, `start` string? = `null`, `end` string? = `null` |
| `index_stats` | `query` string, `start` string? = `null`, `end` string? = `null` |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `LOKI_BASE_URL` | `http://host.docker.internal:3100` | Loki or Loki gateway base URL. |
| `LOKI_BEARER_TOKEN` | empty | Optional bearer token. |
| `LOKI_USERNAME` | empty | Optional basic auth username. |
| `LOKI_PASSWORD` | empty | Optional basic auth password. |
| `LOKI_TENANT_ID` | empty | Optional Loki multi-tenant `X-Scope-OrgID` header. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The Kubernetes profile reads `LOKI_BASE_URL` from a ConfigMap, defaulting to `http://loki-gateway.monitoring.svc.cluster.local`. It is cluster-native as long as that Loki endpoint is reachable from the `mcp-servers` namespace.

