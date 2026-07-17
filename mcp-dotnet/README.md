# mcp-dotnet

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for the .NET SDK over Streamable HTTP.

## Lineage

- Upstream / inspiration: `jongalloway/dotnet-mcp`.
- Strategy: C# wrapper around the `dotnet` CLI rather than a direct source copy.
- Runtime requirement: .NET SDK is installed in the image, and projects must be mounted into the container.
- Trusted-local Docker defaults enable mutating commands such as `dotnet add package` and `dotnet format`.

## Build

```powershell
docker build -t local/mcp-dotnet .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-dotnet:latest` as `airgap/local-mcp-dotnet.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-dotnet -Port 8084
```

Connect MCP clients with Streamable HTTP at `http://localhost:8084/mcp` or legacy SSE at `http://localhost:8084/sse`. Trusted-local images enable mutating operations such as `add package` and `format` by default.

## Tools

| Tool | What it does |
| --- | --- |
| `sdk_info` | Runs `dotnet --info`. |
| `list_projects` | Finds `.csproj`, `.fsproj`, `.vbproj`, and `.sln` files under a path. |
| `restore` | Runs `dotnet restore`. |
| `build` | Runs a complete `dotnet build`, including restore when needed. |
| `test` | Runs a complete `dotnet test`, including restore and build when needed. |
| `add_package` | Runs `dotnet add package`, optionally with a version. |
| `format` | Runs `dotnet format`. |

## API Reference

Command tools return `{ "exitCode": number, "stdout": string, "stderr": string }`.

| Tool | Arguments | Notes |
| --- | --- | --- |
| `sdk_info` | none | Runs from `/workspace`. |
| `list_projects` | `path` string = `.`, `limit` int = `2000` | Returns up to 100,000 project/solution entries. |
| `restore` | `projectOrSolutionPath` string, `timeoutMs` int = `600000` | Runs `dotnet restore`. |
| `build` | `projectOrSolutionPath` string, `configuration` string = `Debug`, `timeoutMs` int = `600000` | Runs a complete `dotnet build`. |
| `test` | `projectOrSolutionPath` string, `configuration` string = `Debug`, `timeoutMs` int = `900000` | Runs a complete `dotnet test`. |
| `add_package` | `projectPath` string, `packageName` string, `version` string? = `null` | Runs `dotnet add package`. |
| `format` | `projectOrSolutionPath` string, `timeoutMs` int = `600000` | Runs `dotnet format`; command timeout can be raised to 24 hours. |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots for project paths. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `MCP_ENABLE_DOTNET_WRITES` | `true` in Dockerfile | Optional compatibility switch; set `false` to block `add_package` and `format`. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The Kubernetes profile mounts a PVC at `/workspace`, sets `MCP_ALLOWED_DIRS=/workspace`, and keeps .NET write tools enabled. `sdk_info`, project discovery, build, and test work with mounted source. `restore` and `add_package` need a preloaded NuGet cache or an internal NuGet feed in air-gapped clusters.
