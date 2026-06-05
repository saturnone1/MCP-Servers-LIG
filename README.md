# MCP Remote Server Bundle

Korean version: [README.ko.md](README.ko.md)

This workspace contains seven independent Docker-buildable remote MCP servers. Each server is implemented as a C#/.NET ASP.NET Core app using `ModelContextProtocol.AspNetCore`, exposes Streamable HTTP at `/mcp`, supports legacy SSE at `/sse` and `/message`, listens on container port `8080`, and provides `/healthz` for Docker health checks.

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

## Build All

```powershell
$servers = 'mcp-office','mcp-filesystem','mcp-git','mcp-shell','mcp-mssql','mcp-dotnet','mcp-hwp'
foreach ($server in $servers) {
  docker build -t "local/$server" $server
}
```

Runtime images are designed to run without internet access. Build-time package restore and upstream downloads are allowed.

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

## Per-Server Docs

Each folder contains a dedicated `README.md` with implementation notes, tool lists, environment variables, and run examples:

- `mcp-office/README.md`
- `mcp-filesystem/README.md`
- `mcp-git/README.md`
- `mcp-shell/README.md`
- `mcp-dotnet/README.md`
- `mcp-mssql/README.md`
- `mcp-hwp/README.md`
