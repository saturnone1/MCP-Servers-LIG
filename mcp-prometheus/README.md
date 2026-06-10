# mcp-prometheus

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Prometheus HTTP API access over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: none.
- Strategy: direct C# MCP server calling the Prometheus HTTP API.
- Runtime requirement: `PROMETHEUS_BASE_URL` must point to a reachable Prometheus server.

## Build

```powershell
docker build -t local/mcp-prometheus .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-prometheus:latest` as `airgap/local-mcp-prometheus.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 8089:8080 `
  -e "PROMETHEUS_BASE_URL=http://host.docker.internal:9090" `
  local/mcp-prometheus
```

Connect MCP clients with Streamable HTTP at `http://localhost:8089/mcp` or legacy SSE at `http://localhost:8089/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `config` | Returns the configured Prometheus base URL. |
| `ready` | Calls `/-/ready`. |
| `query` | Runs instant queries. |
| `query_range` | Runs range queries. |
| `labels` | Lists labels. |
| `label_values` | Lists label values. |
| `targets` | Lists scrape targets. |
| `alerts` | Lists alerts. |
| `series` | Finds matching series. |

## API Reference

Tools return `{ "statusCode": number, "success": bool, "body": string }`.

| Tool | Arguments |
| --- | --- |
| `config` | none |
| `ready` | none |
| `query` | `query` string, `time` string? = `null`, `timeoutSeconds` int = `30` |
| `query_range` | `query` string, `start` string, `end` string, `step` string, `timeoutSeconds` int = `60` |
| `labels` | `start` string? = `null`, `end` string? = `null`, `match` string array? = `null` |
| `label_values` | `label` string, `start` string? = `null`, `end` string? = `null`, `match` string array? = `null` |
| `targets` | `state` string = `any` |
| `alerts` | none |
| `series` | `match` string array, `start` string? = `null`, `end` string? = `null` |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The Kubernetes profile reads `PROMETHEUS_BASE_URL` from a ConfigMap, defaulting to `http://prometheus-server.monitoring.svc.cluster.local:9090`. It is cluster-native as long as that Prometheus service is reachable from the `mcp-servers` namespace.
