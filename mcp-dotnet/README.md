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

## Run

```powershell
docker run --rm -p 8084:8080 -v ${PWD}:/workspace local/mcp-dotnet
```

Connect MCP clients with Streamable HTTP at `http://localhost:8084/mcp` or legacy SSE at `http://localhost:8084/sse`. Trusted-local images enable mutating operations such as `add package` and `format` by default.

## Tools

| Tool | What it does |
| --- | --- |
| `sdk_info` | Runs `dotnet --info`. |
| `list_projects` | Finds `.csproj`, `.fsproj`, `.vbproj`, and `.sln` files under a path. |
| `restore` | Runs `dotnet restore`. |
| `build` | Runs `dotnet build --no-restore`. |
| `test` | Runs `dotnet test --no-build`. |
| `add_package` | Runs `dotnet add package`, optionally with a version. |
| `format` | Runs `dotnet format`. |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots for project paths. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `MCP_ENABLE_DOTNET_WRITES` | `true` in Dockerfile | Optional compatibility switch; set `false` to block `add_package` and `format`. |
