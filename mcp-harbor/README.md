# mcp-harbor

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for the Harbor v2 registry API over Streamable HTTP and legacy SSE.

## Lineage

- Harbor publishes no official MCP server. Two community servers exist and both describe themselves as experimental: [bupd/harbor-mcp-server](https://github.com/bupd/harbor-mcp-server) (13 read-oriented tools: health, statistics, project summary, members, quotas, configurations, volumes, search) and [nomagicln/mcp-harbor](https://github.com/nomagicln/mcp-harbor) (11 CRUD tools: projects, repositories, tags, Helm charts).
- Strategy: combine both surfaces in one C# server and close the gap neither of them covers. Harbor v2 replaced the tag-centric model with **artifacts**, so this server adds artifact listing, artifact detail, tag attachment, vulnerability reports, build history, and scan triggering, plus labels, audit logs, registries, replication, scanners, and webhook policies.
- Helm charts: Harbor v2 stores charts as OCI artifacts rather than in the retired ChartMuseum backend, so `list_artifacts` covers them. The legacy `chartrepo` endpoints from `nomagicln/mcp-harbor` are deliberately not reimplemented.
- Runtime requirement: `HARBOR_BASE_URL` must point to a reachable Harbor instance. `HARBOR_USERNAME` and `HARBOR_PASSWORD` carry a user account, a CLI secret, or a robot account.

## Build

```powershell
docker build -t local/mcp-harbor .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-harbor:latest` as `airgap/local-mcp-harbor.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 127.0.0.1:8101:8080 `
  -e "HARBOR_BASE_URL=https://harbor.example.local" `
  -e "HARBOR_USERNAME=<user>" `
  -e "HARBOR_PASSWORD=<password-or-cli-secret>" `
  local/mcp-harbor
```

Connect MCP clients with Streamable HTTP at `http://localhost:8101/mcp` or legacy SSE at `http://localhost:8101/sse`.

## Tools

| Group | Tools |
| --- | --- |
| Instance | `config`, `get_health`, `get_system_info`, `get_statistics`, `get_volumes`, `search` |
| Projects | `list_projects`, `get_project`, `get_project_summary`, `list_project_members`, `create_project`, `delete_project` |
| Repositories | `list_repositories`, `get_repository`, `delete_repository` |
| Artifacts | `list_artifacts`, `get_artifact`, `delete_artifact`, `get_build_history` |
| Tags | `list_artifact_tags`, `create_tag`, `delete_tag` |
| Security | `get_vulnerabilities`, `scan_artifact`, `list_scanners` |
| Governance | `list_quotas`, `list_labels`, `list_audit_logs`, `list_webhook_policies` |
| Replication | `list_registries`, `list_replication_policies`, `list_replication_executions`, `start_replication` |
| System | `get_configurations`, `update_configurations` |

`get_artifact` and `list_artifacts` accept either a tag name or a digest as the reference, and expose `with_scan_overview` so a vulnerability summary can be fetched in the same call.

`update_configurations` requires `confirm=true` in addition to `MCP_ENABLE_HARBOR_WRITES`, because a wrong authentication value in the Harbor configuration can lock every user out of the instance. Review `get_configurations` first.

Nested repository names such as `team/service` are sent as a single percent-encoded path segment, which is how the Harbor API addresses them.

List tools default to a page size of 50 and clamp to 100. Every list tool exposes `page`.

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `HARBOR_BASE_URL` | `http://harbor.local` | Harbor base URL. The server appends `/api/v2.0` itself. |
| `HARBOR_USERNAME` | empty | Harbor account, CLI secret owner, or robot account name. |
| `HARBOR_PASSWORD` | empty | Password, CLI secret, or robot token. Sent as HTTP basic auth. |
| `MCP_ENABLE_HARBOR_WRITES` | `true` in Dockerfile | Set `false` to block every create, delete, scan, replication, and configuration tool. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The server is cluster-native when the Harbor endpoint is reachable from the `mcp-servers` namespace and the credentials are provided as a Secret.
