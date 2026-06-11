# mcp-gitlab

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for GitLab REST API operations over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: none.
- Strategy: direct C# MCP server calling the GitLab REST API.
- Runtime requirement: `GITLAB_BASE_URL` must point to a reachable GitLab instance. `GITLAB_TOKEN` is required for private resources and writes.

## Build

```powershell
docker build -t local/mcp-gitlab .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-gitlab:latest` as `airgap/local-mcp-gitlab.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 8091:8080 `
  -e "GITLAB_BASE_URL=https://gitlab.example.local" `
  -e "GITLAB_TOKEN=<token>" `
  local/mcp-gitlab
```

Connect MCP clients with Streamable HTTP at `http://localhost:8091/mcp` or legacy SSE at `http://localhost:8091/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `config` | Returns GitLab base URL and token presence. |
| `list_projects` | Lists visible projects. |
| `get_project` | Gets one project by id or path. |
| `list_issues` | Lists project issues. |
| `create_issue` | Creates a project issue. |
| `list_merge_requests` | Lists project merge requests. |
| `get_file` | Reads a repository file. |
| `create_or_update_file` | Creates or updates a repository file. |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `GITLAB_BASE_URL` | `http://gitlab.local` | GitLab base URL. |
| `GITLAB_TOKEN` | empty | Personal/project/group access token. |
| `MCP_ENABLE_GITLAB_WRITES` | `true` in Dockerfile | Set `false` to block issue and file writes. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The server is cluster-native when the GitLab endpoint is reachable from the `mcp-servers` namespace and the token is provided as a Secret.

