# mcp-docker

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Docker operations over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: none.
- Strategy: direct C# MCP server wrapping the Docker CLI.
- Runtime requirement: mount the Docker socket into the container.
- Trusted-local Docker defaults enable container and image mutation tools.

## Build

```powershell
docker build -t local/mcp-docker .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-docker:latest` as `airgap/local-mcp-docker.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 127.0.0.1:8088:8080 `
  -v /var/run/docker.sock:/var/run/docker.sock `
  local/mcp-docker
```

Connect MCP clients with Streamable HTTP at `http://localhost:8088/mcp` or legacy SSE at `http://localhost:8088/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `version` | Returns Docker client/server version. |
| `list_containers` | Lists containers. |
| `list_images` | Lists images. |
| `inspect` | Inspects a container or image. |
| `logs` | Reads container logs. |
| `run_container` | Runs a container. |
| `start_container` | Starts a container. |
| `stop_container` | Stops a container. |
| `remove_container` | Removes a container. |
| `pull_image` | Pulls an image. |
| `remove_image` | Removes an image. |

## API Reference

All tools return `{ "exitCode": number, "stdout": string, "stderr": string }`.

| Tool | Arguments |
| --- | --- |
| `version` | none |
| `list_containers` | `all` bool = `true`, `format` string = `json` |
| `list_images` | `format` string = `json` |
| `inspect` | `target` string |
| `logs` | `container` string, `tail` int = `200`, `timestamps` bool = `false` |
| `run_container` | `image` string, `name` string? = `null`, `args` string array? = `null`, `detach` bool = `true`, `ports` string array? = `null`, `volumes` string array? = `null`, `environment` string array? = `null` |
| `start_container` | `container` string |
| `stop_container` | `container` string, `timeoutSeconds` int = `10` |
| `remove_container` | `container` string, `force` bool = `false` |
| `pull_image` | `image` string |
| `remove_image` | `image` string, `force` bool = `false` |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ENABLE_DOCKER_WRITES` | `true` | Set `false` to block container/image start, stop, create, remove, and pull operations. |
| `DOCKER_PATH` | `docker` | Docker CLI executable path. |

## Kubernetes

No Kubernetes manifests are provided for `mcp-docker` in this phase. The server depends on Docker daemon access through `/var/run/docker.sock`; many Kubernetes clusters use containerd without a Docker socket, and mounting the host socket would grant high-privilege node-level container control.
