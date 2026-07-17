# mcp-filesystem

Korean version: [README.ko.md](README.ko.md)

C# remote MCP filesystem server over Streamable HTTP.

## Lineage

- Upstream / reference behavior: `mark3labs/mcp-filesystem-server`
- Strategy: direct C# reimplementation using `System.IO`.
- Security model retained as optional knobs: allowed roots, path normalization, symlink-aware canonical paths, and write gating.
- Trusted-local Docker defaults open the gates for local testing: writes enabled and `MCP_ALLOWED_DIRS=/`.

## Build

```powershell
docker build -t local/mcp-filesystem .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-filesystem:latest` as `airgap/local-mcp-filesystem.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-filesystem -Port 8081
```

Connect MCP clients with Streamable HTTP at `http://localhost:8081/mcp` or legacy SSE at `http://localhost:8081/sse`. Trusted-local images enable writes by default and allow `/` inside the container unless `MCP_ALLOWED_DIRS` overrides it.

## Tools

| Tool | What it does |
| --- | --- |
| `list_allowed_directories` | Lists allowed container root directories. |
| `read_file` | Reads a UTF-8 text file. |
| `read_multiple_files` | Reads several UTF-8 text files in one call. |
| `write_file` | Creates or overwrites a UTF-8 text file. |
| `copy` | Copies a file or directory. |
| `move` | Moves a file or directory. |
| `delete` | Deletes a file or directory. |
| `stat` | Returns file or directory metadata. |
| `list_directory` | Lists directory entries with pattern, recursion, and limit options. |
| `search` | Searches file names using a regular expression. |

## API Reference

Path arguments accept normal container paths or Windows host paths when `MCP_PATH_MAPPINGS` is configured.

| Tool | Arguments | Returns |
| --- | --- | --- |
| `list_allowed_directories` | none | string array of allowed roots. |
| `read_file` | `path` string, `maxBytes` int = `16777216` | File text (up to 64 MiB). |
| `read_multiple_files` | `paths` string array, `maxBytesPerFile` int = `16777216` | Object keyed by path with file text values (up to 64 MiB each). |
| `write_file` | `path` string, `content` string | Write metadata. |
| `copy` | `sourcePath` string, `destinationPath` string, `overwrite` bool = `false` | Copy metadata. |
| `move` | `sourcePath` string, `destinationPath` string, `overwrite` bool = `false` | Move metadata. |
| `delete` | `path` string, `recursive` bool = `false` | Delete metadata. |
| `stat` | `path` string | File or directory metadata. |
| `list_directory` | `path` string = `.`, `pattern` string = `*`, `recursive` bool = `false`, `limit` int = `2000` | Entry metadata array (up to 100,000). |
| `search` | `path` string, `regex` string, `limit` int = `1000` | Matching entry metadata array (up to 100,000). |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `MCP_ENABLE_WRITES` | `true` in Dockerfile | Optional compatibility switch; set `false` to block write/copy/move/delete. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The Kubernetes profile mounts a PVC at `/workspace`, sets `MCP_ALLOWED_DIRS=/workspace`, and keeps write tools enabled. This server is fully compatible with Linux Kubernetes when backed by a PVC or another cluster-native volume.
