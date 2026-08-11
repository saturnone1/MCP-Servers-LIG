# mcp-jira

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Jira REST API operations over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: none.
- Strategy: direct C# MCP server calling the Jira REST API.
- Runtime requirement: `JIRA_BASE_URL` must point to a reachable Jira instance. Use either `JIRA_BEARER_TOKEN` or `JIRA_EMAIL` plus `JIRA_API_TOKEN` for authentication.

## Build

```powershell
docker build -t local/mcp-jira .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-jira:latest` as `airgap/local-mcp-jira.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 127.0.0.1:8092:8080 `
  -e "JIRA_BASE_URL=https://jira.example.local" `
  -e "JIRA_BEARER_TOKEN=<token>" `
  local/mcp-jira
```

Connect MCP clients with Streamable HTTP at `http://localhost:8092/mcp` or legacy SSE at `http://localhost:8092/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `config` | Returns Jira base URL and auth configuration status. |
| `search_issues` | Searches issues with JQL. |
| `get_issue` | Gets one issue by key. |
| `create_issue` | Creates an issue. |
| `add_comment` | Adds a comment to an issue. |
| `list_transitions` | Lists available issue transitions. |
| `transition_issue` | Transitions an issue. |
| `list_projects` | Lists Jira projects. |

`search_issues` defaults to 100 results and accepts `startAt` for unrestricted pagination beyond Jira's 100-item request limit.

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `JIRA_BASE_URL` | `http://jira.local` | Jira base URL. |
| `JIRA_API_VERSION` | `3` | REST API version. Use `2` for Jira Server/Data Center deployments that do not expose v3. |
| `JIRA_BEARER_TOKEN` | empty | Bearer token for Jira Data Center or compatible deployments. |
| `JIRA_EMAIL` | empty | Email for Jira Cloud basic auth. |
| `JIRA_API_TOKEN` | empty | API token for Jira Cloud basic auth. |
| `MCP_ENABLE_JIRA_WRITES` | `true` in Dockerfile | Set `false` to block issue/comment/transition writes. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The server is cluster-native when the Jira endpoint is reachable from the `mcp-servers` namespace and credentials are provided as a Secret.

