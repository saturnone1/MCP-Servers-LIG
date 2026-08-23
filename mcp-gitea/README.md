# mcp-gitea

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Gitea API v1 operations over Streamable HTTP and legacy SSE.

## Lineage

- Reference surface: the official [gitea/gitea-mcp](https://gitea.com/gitea/gitea-mcp) server (Go, MIT). Its tool catalogue was used as the specification.
- Strategy: direct C# MCP server calling the Gitea API v1, so the server keeps the same shape as the other servers in this repository (`/healthz`, `/mcp`, `/sse`, one shared .NET runtime, one Dockerfile base image).
- Runtime requirement: `GITEA_BASE_URL` must point to a reachable Gitea instance. `GITEA_TOKEN` is required for private resources and for every write tool.

## Build

```powershell
docker build -t local/mcp-gitea .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-gitea:latest` as `airgap/local-mcp-gitea.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 127.0.0.1:8099:8080 `
  -e "GITEA_BASE_URL=https://gitea.example.local" `
  -e "GITEA_TOKEN=<token>" `
  local/mcp-gitea
```

Connect MCP clients with Streamable HTTP at `http://localhost:8099/mcp` or legacy SSE at `http://localhost:8099/sse`.

## Tools

| Group | Tools |
| --- | --- |
| Server | `config`, `get_version`, `get_me`, `list_my_orgs`, `list_notifications` |
| Search | `search_users`, `search_repos`, `search_issues` |
| Repositories | `list_my_repos`, `list_org_repos`, `get_repo`, `create_repo`, `fork_repo` |
| Branches and tags | `list_branches`, `create_branch`, `delete_branch`, `list_tags` |
| Commits | `list_commits`, `get_commit`, `get_repository_tree` |
| Files | `get_dir_contents`, `get_file_contents`, `create_or_update_file`, `delete_file` |
| Partial edits | `append_to_file`, `prepend_to_file`, `replace_in_file` |
| Issues | `list_issues`, `get_issue`, `create_issue`, `edit_issue`, `list_issue_comments`, `create_issue_comment` |
| Pull requests | `list_pull_requests`, `get_pull_request`, `get_pull_request_diff`, `create_pull_request`, `merge_pull_request` |
| Releases | `list_releases`, `get_latest_release`, `create_release` |
| Metadata | `list_labels`, `list_milestones`, `wiki_read`, `list_action_runs` |

`get_file_contents` decodes Gitea's base64 payload into text by default; pass `decode=false` to get the raw metadata object with the blob sha.

The partial-edit tools read the current blob, apply one change, and commit the result with the existing sha, so a large file can be edited without the model regenerating the whole body.

List tools default to a page size of 50 and clamp to Gitea's maximum of 100. Every list tool exposes `page` for unrestricted pagination.

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `GITEA_BASE_URL` | `http://gitea.local` | Gitea base URL. |
| `GITEA_TOKEN` | empty | Personal access token, sent as `Authorization: token <value>`. |
| `MCP_ENABLE_GITEA_WRITES` | `true` in Dockerfile | Set `false` to block every create, edit, merge, and delete tool. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The server is cluster-native when the Gitea endpoint is reachable from the `mcp-servers` namespace and the token is provided as a Secret.
