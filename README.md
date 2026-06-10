# MCP Remote Server Bundle

Korean version: [README.ko.md](README.ko.md)

This workspace contains ten independent Docker-buildable remote MCP servers. Each server is implemented as a C#/.NET ASP.NET Core app using `ModelContextProtocol.AspNetCore`, exposes Streamable HTTP at `/mcp`, supports legacy SSE at `/sse` and `/message`, listens on container port `8080`, and provides `/healthz` for Docker health checks.

These images are intended for trusted local testing. Write and execute capabilities are enabled by default, and allowed paths default to `/` inside the container. Host filesystem access is still limited by Docker volume mounts.

## Server Matrix

| Server | Port | Upstream / lineage | Implementation strategy | Main capabilities |
| --- | ---: | --- | --- | --- |
| `mcp-office` | 8080 | `iOfficeAI/OfficeCLI` | Wrap bundled OfficeCLI plus `antiword` for legacy `.doc` | Inspect/read Office docs, extract text, create docs, apply batch edits, render/export, raw OfficeCLI |
| `mcp-filesystem` | 8081 | `mark3labs/mcp-filesystem-server` security model | C# reimplementation with `System.IO` | Read/write/copy/move/delete files, stat, list/search directories, allowed-root handling |
| `mcp-git` | 8082 | `modelcontextprotocol/servers` Git server behavior | C# wrapper around the `git` CLI | Status, log, diff, show, branch list, blame, grep, init/add/commit/checkout |
| `mcp-shell` | 8083 | New local implementation | C# `ProcessStartInfo` command runner | Run local container commands with timeout, output limit, optional command/env allowlists |
| `mcp-dotnet` | 8084 | Inspired by `jongalloway/dotnet-mcp` | C# wrapper around the `dotnet` CLI | SDK info, project discovery, restore/build/test, add package, format |
| `mcp-mssql` | 8085 | Based on `little-fort/mcp-dotnet-mssql` behavior | C# SQL Server tools using `Microsoft.Data.SqlClient` | List databases/schemas/tables, describe tables, read queries, non-query SQL |
| `mcp-hwp` | 8086 | Local implementation using open tooling | C# server using `pyhwp`/`hwp5txt`, LibreOffice, and ZIP/XML parsing | Extract `.hwp`/`.hwpx` text, inspect files, convert to `txt/docx/pdf/odt` |
| `mcp-kubernetes` | 8087 | Local implementation | C# wrapper around `kubectl` | Cluster info, namespaces, pods, logs, deployments, YAML apply/delete/restart/scale/generate |
| `mcp-docker` | 8088 | Local implementation | C# wrapper around Docker CLI and Docker socket | Containers, images, inspect, logs, run/start/stop/remove, pull/remove image |
| `mcp-prometheus` | 8089 | Local implementation | C# Prometheus HTTP API client | Readiness, instant/range queries, labels, targets, alerts, series |

## Connections

Each image listens on port `8080` inside the container. The smoke-test port layout is:

| Server | Streamable HTTP | Legacy SSE |
| --- | --- | --- |
| `mcp-office` | `http://localhost:8080/mcp` | `http://localhost:8080/sse` |
| `mcp-filesystem` | `http://localhost:8081/mcp` | `http://localhost:8081/sse` |
| `mcp-git` | `http://localhost:8082/mcp` | `http://localhost:8082/sse` |
| `mcp-shell` | `http://localhost:8083/mcp` | `http://localhost:8083/sse` |
| `mcp-dotnet` | `http://localhost:8084/mcp` | `http://localhost:8084/sse` |
| `mcp-mssql` | `http://localhost:8085/mcp` | `http://localhost:8085/sse` |
| `mcp-hwp` | `http://localhost:8086/mcp` | `http://localhost:8086/sse` |
| `mcp-kubernetes` | `http://localhost:8087/mcp` | `http://localhost:8087/sse` |
| `mcp-docker` | `http://localhost:8088/mcp` | `http://localhost:8088/sse` |
| `mcp-prometheus` | `http://localhost:8089/mcp` | `http://localhost:8089/sse` |

## MCP API Shape

All servers expose the same MCP transport API. Tool discovery uses `tools/list`; tool execution uses `tools/call` against the server's `/mcp` endpoint. Legacy clients can connect through `/sse` and send messages to `/message`.

Example Streamable HTTP call:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "extract_text",
    "arguments": {
      "path": "C:\\Users\\taewon\\Desktop\\넥스원\\2024 분산스위치 논문.hwp",
      "maxChars": 4000
    }
  }
}
```

Each per-server README includes the exact tool names, parameters, defaults, and return shape.

## Build All

```powershell
$servers = 'mcp-office','mcp-filesystem','mcp-git','mcp-shell','mcp-mssql','mcp-dotnet','mcp-hwp','mcp-kubernetes','mcp-docker','mcp-prometheus'
foreach ($server in $servers) {
  docker build -t "local/$server" $server
}
```

Runtime images are designed to run without internet access. Build-time package restore and upstream downloads are allowed.

## Export For Air Gap

Build or prepare the images on an internet-connected machine, then export them as Docker tar archives:

```powershell
.\scripts\export-airgap.ps1
```

To rebuild before exporting:

```powershell
.\scripts\export-airgap.ps1 -Build
```

The script writes one archive per server:

```text
mcp-office\airgap\local-mcp-office.tar
mcp-filesystem\airgap\local-mcp-filesystem.tar
mcp-git\airgap\local-mcp-git.tar
mcp-shell\airgap\local-mcp-shell.tar
mcp-dotnet\airgap\local-mcp-dotnet.tar
mcp-mssql\airgap\local-mcp-mssql.tar
mcp-hwp\airgap\local-mcp-hwp.tar
mcp-kubernetes\airgap\local-mcp-kubernetes.tar
mcp-docker\airgap\local-mcp-docker.tar
mcp-prometheus\airgap\local-mcp-prometheus.tar
```

Copy the needed `airgap` folder or tar files to the air-gapped machine and load them with `docker load -i <tar-file>`. Each server folder has an `airgap/README.ko.md` with the exact load and run commands. Tar archives are ignored by Git.

## Run All Smoke Containers

```powershell
.\tests\mcp-smoke.ps1
```

The smoke test builds the images, restarts the containers, verifies `/healthz`, checks SSE, lists MCP tools, and calls representative tools. `MSSQL_CONNECTION_STRING` is optional; without it, the MSSQL server is started and tool discovery is tested, but live SQL execution is skipped.

To start all servers without running the smoke calls:

```powershell
.\scripts\run-all.ps1
```

By default, this mounts the repository at `/workspace` and the Windows `C:\` drive at `/host/c`, then sets `MCP_PATH_MAPPINGS=C:\=/host/c`. That lets MCP clients pass normal Windows paths such as `C:\Users\taewon\Desktop\넥스원\2024 분산스위치 논문.hwp`.

## Path Mapping

For Linux containers to accept Windows host paths from MCP clients, the matching host folder must be mounted with Docker and listed in `MCP_PATH_MAPPINGS`:

```powershell
docker run --rm -p 8081:8080 `
  -v C:\:/host/c `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-filesystem
```

The same mapping pattern is supported by path-based servers such as Office, filesystem, Git, shell, .NET, and HWP. `MCP_ALLOWED_DIRS=/` opens the container filesystem, but it does not grant access to host folders that were not mounted into the container.

## Kubernetes Deployment

Kubernetes manifests are provided for the MCP servers that can reasonably run as Linux Kubernetes workloads:

- Included: `mcp-filesystem`, `mcp-git`, `mcp-dotnet`, `mcp-kubernetes`, `mcp-prometheus`
- Excluded in this phase: `mcp-office`, `mcp-shell`, `mcp-hwp`, `mcp-mssql`, `mcp-docker`

Each included server has a `k8s/` folder with namespace, Deployment, Service, and any required ConfigMap/PVC/RBAC files. Apply a server with:

```powershell
kubectl apply -f .\<server>\k8s\
```

The default namespace is `mcp-servers`, container port is `8080`, services are `ClusterIP`, and probes use `GET /healthz`. File/project/repo servers use `/workspace` backed by a PVC instead of broad host-path mounts.

Air-gapped clusters need the images loaded into the cluster runtime or pushed to an internal registry. The manifests use `local/<server>:latest` by default. For single-node Docker-based clusters this may work after `docker load`; for containerd or multi-node clusters, import the image into each node runtime or rewrite the image reference to a private registry path.

`mcp-docker` is excluded from the default Kubernetes manifests. It requires access to a Docker daemon socket, which is unavailable in many containerd-based clusters and is high-privilege when host-mounted.

## Per-Server Docs

Each folder contains a dedicated `README.md` with implementation notes, tool lists, environment variables, and run examples:

- `mcp-office/README.md`
- `mcp-filesystem/README.md`
- `mcp-git/README.md`
- `mcp-shell/README.md`
- `mcp-dotnet/README.md`
- `mcp-mssql/README.md`
- `mcp-hwp/README.md`
- `mcp-kubernetes/README.md`
- `mcp-docker/README.md`
- `mcp-prometheus/README.md`
