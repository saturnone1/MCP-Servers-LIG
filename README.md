# MCP Remote Server Bundle

Korean version: [README.ko.md](README.ko.md)

This workspace contains fifteen general remote MCP servers plus five Windows-host or data-processing MCP servers. Each server is implemented as a C#/.NET ASP.NET Core app using `ModelContextProtocol.AspNetCore`, exposes Streamable HTTP at `/mcp`, supports legacy SSE at `/sse` and `/message`, and provides `/healthz`.

These images are intended for trusted local testing. Write and execute capabilities are enabled by default, and allowed paths default to `/` inside the container. Host filesystem access is still limited by Docker volume mounts.

## Server Matrix

| Server | Port | Upstream / lineage | Implementation strategy | Main capabilities |
| --- | ---: | --- | --- | --- |
| `mcp-office` | 42180 | `iOfficeAI/OfficeCLI` | Wrap bundled OfficeCLI plus `antiword` for legacy `.doc` | Inspect/read Office docs, extract text, create docs, apply batch edits, render/export, raw OfficeCLI |
| `mcp-filesystem` | 42181 | `mark3labs/mcp-filesystem-server` security model | C# reimplementation with `System.IO` | Read/write/copy/move/delete files, stat, list/search directories, allowed-root handling |
| `mcp-git` | 42182 | `modelcontextprotocol/servers` Git server behavior | C# wrapper around the `git` CLI | Status, log, diff, show, branch list, blame, grep, init/add/commit/checkout |
| `mcp-shell` | 42183 | New local implementation | C# `ProcessStartInfo` command runner | Run local container commands with timeout, output limit, optional command/env allowlists |
| `mcp-dotnet` | 42184 | Inspired by `jongalloway/dotnet-mcp` | C# wrapper around the `dotnet` CLI | SDK info, project discovery, restore/build/test, add package, format |
| `mcp-mssql` | 42185 | Based on `little-fort/mcp-dotnet-mssql` behavior | C# SQL Server tools using `Microsoft.Data.SqlClient` | List databases/schemas/tables, describe tables, read queries, non-query SQL |
| `mcp-hwp` | 42186 | Local implementation using open tooling | C# server using `pyhwp`/`hwp5txt`, LibreOffice, and ZIP/XML parsing | Extract `.hwp`/`.hwpx` text, inspect files, convert to `txt/docx/pdf/odt` |
| `mcp-kubernetes` | 42187 | Local implementation | C# wrapper around `kubectl` | Cluster info, namespaces, pods, logs, deployments, YAML apply/delete/restart/scale/generate |
| `mcp-docker` | 42188 | Local implementation | C# wrapper around Docker CLI and Docker socket | Containers, images, inspect, logs, run/start/stop/remove, pull/remove image |
| `mcp-prometheus` | 42189 | Local implementation | C# Prometheus HTTP API client | Readiness, instant/range queries, labels, targets, alerts, series |
| `mcp-postgresql` | 42190 | Local implementation | C# PostgreSQL tools using `Npgsql` | List databases/schemas/tables, describe tables, read queries, non-query SQL |
| `mcp-gitlab` | 42191 | Local implementation | C# GitLab REST API client | Projects, issues, merge requests, repository files |
| `mcp-jira` | 42192 | Local implementation | C# Jira REST API client | JQL search, issues, comments, transitions, projects |
| `mcp-loki` | 42193 | Local implementation | C# Loki HTTP API client | LogQL queries, recent log search, labels, series, index stats |
| `mcp-confluence` | 42198 | Local implementation | C# Confluence Data Center REST API v1 client | Spaces, CQL content search, pages, child pages, create/update/delete pages |
| `mcp-rhapsody` | 42194 | Local implementation | Windows-host C# server for Rhapsody COM/CLI/file automation | Detect Rhapsody, inspect model files, run configured CLI |
| `mcp-matlab` | 42195 | Official `matlab/matlab-mcp-core-server` lineage | Windows-host C# wrapper around MATLAB CLI/COM plus official MCP bridge hook | Detect MATLAB, run batch/script, COM eval, workspace summary |
| `mcp-autocad` | 42196 | Open-source AutoCAD MCP COM automation pattern | Windows-host C# AutoCAD COM wrapper | Open drawings, list layers/entities, send commands, create layer/line, save |
| `mcp-solidworks` | 42197 | Open-source SolidWorks MCP COM automation pattern | Windows-host C# SolidWorks COM wrapper | Open CAD docs, list features/components, mass properties, rebuild/save/export |
| `mcp-pdf` | 42199 | Local implementation using Docling | C# PDF dataset controller with local/remote Docling adapters | Async ingest, OCR/text/table/image extraction, structural chunks, evidence reading, embeddings, SQLite/PostgreSQL/Qdrant, JSONL/Parquet |

## Connections

Docker images listen on port `8080` inside the container. Windows-host desktop servers listen directly on their listed localhost ports. The smoke-test port layout is:

| Server | Streamable HTTP | Legacy SSE |
| --- | --- | --- |
| `mcp-office` | `http://localhost:42180/mcp` | `http://localhost:42180/sse` |
| `mcp-filesystem` | `http://localhost:42181/mcp` | `http://localhost:42181/sse` |
| `mcp-git` | `http://localhost:42182/mcp` | `http://localhost:42182/sse` |
| `mcp-shell` | `http://localhost:42183/mcp` | `http://localhost:42183/sse` |
| `mcp-dotnet` | `http://localhost:42184/mcp` | `http://localhost:42184/sse` |
| `mcp-mssql` | `http://localhost:42185/mcp` | `http://localhost:42185/sse` |
| `mcp-hwp` | `http://localhost:42186/mcp` | `http://localhost:42186/sse` |
| `mcp-kubernetes` | `http://localhost:42187/mcp` | `http://localhost:42187/sse` |
| `mcp-docker` | `http://localhost:42188/mcp` | `http://localhost:42188/sse` |
| `mcp-prometheus` | `http://localhost:42189/mcp` | `http://localhost:42189/sse` |
| `mcp-postgresql` | `http://localhost:42190/mcp` | `http://localhost:42190/sse` |
| `mcp-gitlab` | `http://localhost:42191/mcp` | `http://localhost:42191/sse` |
| `mcp-jira` | `http://localhost:42192/mcp` | `http://localhost:42192/sse` |
| `mcp-loki` | `http://localhost:42193/mcp` | `http://localhost:42193/sse` |
| `mcp-confluence` | `http://localhost:42198/mcp` | `http://localhost:42198/sse` |
| `mcp-rhapsody` | `http://localhost:42194/mcp` | `http://localhost:42194/sse` |
| `mcp-matlab` | `http://localhost:42195/mcp` | `http://localhost:42195/sse` |
| `mcp-autocad` | `http://localhost:42196/mcp` | `http://localhost:42196/sse` |
| `mcp-solidworks` | `http://localhost:42197/mcp` | `http://localhost:42197/sse` |
| `mcp-pdf` | `http://localhost:42199/mcp` | `http://localhost:42199/sse` |

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
$servers = 'mcp-office','mcp-filesystem','mcp-git','mcp-shell','mcp-mssql','mcp-dotnet','mcp-hwp','mcp-kubernetes','mcp-docker','mcp-prometheus','mcp-postgresql','mcp-gitlab','mcp-jira','mcp-loki','mcp-confluence'
foreach ($server in $servers) {
  docker build -t "local/$server" $server
}
```

Runtime images are designed to run without internet access. Build-time package restore and upstream downloads are allowed.

`mcp-rhapsody` is not part of the Docker build list. It is published as a Windows host package:

```powershell
.\mcp-rhapsody\scripts\publish-win.ps1
```

The MATLAB, AutoCAD, and SolidWorks MCP servers are also Windows-host packages:

```powershell
.\mcp-matlab\scripts\publish-win.ps1
.\mcp-autocad\scripts\publish-win.ps1
.\mcp-solidworks\scripts\publish-win.ps1
```

For MATLAB, run `.\mcp-matlab\scripts\download-official-mcp.ps1` before publishing if you want the official MathWorks MCP server binary copied into the air-gap package under `official/`.

To publish all Windows-host desktop servers at once:

```powershell
.\scripts\publish-windows-host.ps1 -Zip
```

The output folders contain a native Windows `.exe`, `run.ps1`, `start.cmd`, and an editable `.env` file. For example:

```text
windows-host-publish\mcp-matlab-win-x64\McpMatlab.exe
windows-host-publish\mcp-matlab-win-x64\start.cmd
windows-host-publish\mcp-matlab-win-x64\run.ps1
```

Default bundle publish mode is framework-dependent `win-x64` plus one shared bundled .NET/ASP.NET Core runtime under `mcp-bundle\dotnet`. The target PC does not need .NET 10 or ASP.NET Core 10 installed. Use `-BundleDotnetRuntime $false` only when you deliberately want to rely on a preinstalled runtime, or `-SelfContained $true` when you want each server to carry its own runtime.

## Windows Unified EXE Bundle

You can also publish every MCP server, including the servers that normally default to Docker, as Windows `.exe` packages controlled by `mcp-manager`. This does not remove the Dockerfiles, air-gap tar workflow, or Kubernetes YAML; it adds a Windows-local process bundle.

```powershell
.\scripts\publish-mcp-bundle.ps1 -Zip
```

The output is `mcp-bundle`:

```text
mcp-bundle\McpManager.exe
mcp-bundle\servers.json
mcp-bundle\start-all.cmd
mcp-bundle\stop-all.cmd
mcp-bundle\status.cmd
mcp-bundle\urls.cmd
mcp-bundle\mcp-office-win-x64\McpOffice.exe
mcp-bundle\mcp-filesystem-win-x64\McpFilesystem.exe
mcp-bundle\mcp-git-win-x64\McpGit.exe
...
mcp-bundle\mcp-solidworks-win-x64\McpSolidWorks.exe
```

The bundle `servers.json` registers all 20 servers as `process` entries. `McpManager.exe start all` therefore starts each `Mcp*.exe` directly and does not call Docker.

The bundle contains default `<server>.env` files. Editable per-user overrides are stored under `%LOCALAPPDATA%\LIG AI MCP\.mcp-manager\env`; use `edit-env-mcp-jira.cmd` or the Manager dashboard, then restart the server. `McpManager.exe` also reads `common.env` and `<server>.env` from the bundle root plus explicit `envFiles` from `servers.json` before applying the per-user override.

If you patch an existing bundle with the new manager, run `sync-env-files.ps1` once to split existing `servers.json` environment values into per-server `.env` files and create `edit-env-*.cmd` helpers.

Double-click `mcp-bundle\McpManager.exe` with no arguments to open the console menu. The menu supports start/stop/restart all, status, URLs, per-server start/stop, and logs.

Select a server and press `P` to register or unregister it for automatic startup. Registered servers show `[A]`, are started the next time the bundle dashboard opens, and remain listed in `autostart.json` at the bundle root. Servers started by the dashboard stop when the dashboard closes.

```powershell
.\mcp-bundle\McpManager.exe list all
.\mcp-bundle\McpManager.exe start mcp-filesystem
.\mcp-bundle\McpManager.exe status all
.\mcp-bundle\McpManager.exe urls all
.\mcp-bundle\McpManager.exe stop all
.\mcp-bundle\LIG-AI-MCP.cmd env mcp-filesystem
.\mcp-bundle\LIG-AI-MCP.cmd set-env mcp-filesystem MCP_ALLOWED_DIRS "*"
.\mcp-bundle\LIG-AI-MCP.cmd remove-env mcp-filesystem MCP_ALLOWED_DIRS
.\mcp-bundle\LIG-AI-MCP.cmd autostart enable mcp-filesystem
.\mcp-bundle\LIG-AI-MCP.cmd autostart list
```

In the interactive dashboard, select a server and press `E` to edit its environment variables without opening a text editor. Use `A` to add, `Enter` to edit, `D` to delete, `N` to open Notepad, and `B` to go back. Restart the server after changing environment values.

The bundle also creates double-click command files: `start-all.cmd`, `stop-all.cmd`, `status.cmd`, plus per-server `start-mcp-*.cmd` and `stop-mcp-*.cmd`. These launchers call `runtime-env.cmd`, so the bundled shared runtime is used automatically and the target Windows PC does not need .NET installed globally.

Check the bundle structure and external CLI availability with:

```powershell
.\scripts\test-mcp-bundle.ps1
```

Build the Windows installer with:

```powershell
.\scripts\build-installer.ps1
```

The default version comes from `installer\VERSION`. Override it with `-Version` only for an intentional release. For production signing, pass `-CertificateThumbprint <thumbprint>` or set `LIG_SIGNING_CERT_THUMBPRINT`; the build signs product executables, the MSI payload, and the final Setup.

The build writes exactly one user-facing `Setup.exe` to `installer\output`; the MSI is an internal build payload embedded in that executable and is not distributed separately. Setup requests elevation before extracting the payload to a machine-accessible staging directory and invokes Windows Installer with an explicit basic progress window, avoiding non-elevated execution and 2502/2503 errors. Apps and the Start Menu register a dedicated self-elevating Uninstaller that stops installed processes, runs removal with its own progress UI, and shows a topmost completion result. Users do not need the `mcp-bundle` directory, MSI, ZIP archive, WiX, or a separate .NET/ASP.NET Core runtime installer. The installer includes all 20 MCP servers, the shared runtime, a self-contained Manager with the product icon embedded, Start Menu and desktop shortcuts, and rollback-safe upgrades. `McpManager.exe` requests administrator elevation on every launch; servers started by it inherit that token, while individual server executables remain directly launchable by MCP clients. The shortcuts launch `McpManager.exe` directly so the product icon is used on the taskbar. Installation is per-machine under Program Files; writable settings, logs, and PID files stay under `%LOCALAPPDATA%\LIG AI MCP\.mcp-manager`. External applications such as Git, Docker, kubectl, Docling, `pdftoppm`, MATLAB, AutoCAD, SolidWorks, and Rhapsody are still required when their integrations are used. Without a configured certificate the setup remains unsigned and the build prints an explicit warning.

The server executables are included, but tools that shell out to external programs still need those programs on the target PC. Items installed by Dockerfiles through `apt-get`, `curl`, or `pip` are not automatically embedded in the Windows exe bundle.

| Server | Windows exe bundle status | Additional requirement |
| --- | --- | --- |
| `mcp-filesystem` | Self-contained | None |
| `mcp-mssql`, `mcp-postgresql` | Server and shared runtime included | Real DB connection string |
| `mcp-prometheus`, `mcp-gitlab`, `mcp-jira`, `mcp-loki`, `mcp-confluence` | Server and shared runtime included | Real API URL/token |
| `mcp-shell` | Server and shared runtime included | Commands invoked by tools must exist on Windows |
| `mcp-git` | Server and shared runtime included | `git.exe` |
| `mcp-dotnet` | Server runs on the bundled runtime | External .NET SDK/CLI on the target PC; `MCP_DOTNET_CLI_PATH` can select one explicitly. Project `global.json` and target frameworks control .NET 8/9/10 SDK usage. |
| `mcp-kubernetes` | Server and shared runtime included | `kubectl.exe` plus kubeconfig or equivalent cluster auth |
| `mcp-docker` | Server and shared runtime included | Docker CLI and Docker Desktop/daemon |
| `mcp-office` | Bundles `officecli.exe` | `antiword` for legacy `.doc` is optional; OfficeCLI is used as fallback |
| `mcp-hwp` | Built-in parser handles `.hwpx` and basic `.hwp` text extraction | Optional `hwp5txt` for fallback; LibreOffice `soffice` only for `docx/pdf/odt` conversion |
| `mcp-rhapsody`, `mcp-matlab`, `mcp-autocad`, `mcp-solidworks` | Server and shared runtime included | Corresponding commercial software, COM/CLI, and license |

The Office publish flow copies the downloaded OfficeCLI Windows binary from `mcp-office\vendor\officecli` into the bundle as `tools/officecli.exe`. If the vendor binary is missing, `publish-mcp-bundle.ps1` calls `mcp-office\scripts\download-officecli.ps1`.

The MATLAB publish flow copies the downloaded official MathWorks MCP binary from `mcp-matlab\vendor\official` into the bundle's `official/` folder.

To publish only the AutoCAD replacement folder for an existing full bundle:

```powershell
.\scripts\publish-autocad-bundle-patch.ps1 -Zip
```

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
mcp-postgresql\airgap\local-mcp-postgresql.tar
mcp-gitlab\airgap\local-mcp-gitlab.tar
mcp-jira\airgap\local-mcp-jira.tar
mcp-loki\airgap\local-mcp-loki.tar
mcp-confluence\airgap\local-mcp-confluence.tar
```

Copy the needed `airgap` folder or tar files to the air-gapped machine and load them with `docker load -i <tar-file>`. Each server folder has an `airgap/README.ko.md` with the exact load and run commands. Tar archives are ignored by Git.

## Verification

```powershell
.\tests\verify-priority.ps1 -SkipBuild -SkipImagePull
```

The priority verification runner executes the Docker MCP smoke test, external API mock calls, PostgreSQL fixture smoke, SQL Server fixture smoke, and the Windows-host Rhapsody MCP smoke. On a Rhapsody-installed Windows PC, add `-RhapsodyProjectPath "C:\path\model.rpyx"` to include COM read smoke, and add `-RunRhapsodyWriteSmoke` only when it is safe to modify and save that model.

For the Windows-host MATLAB, AutoCAD, and SolidWorks MCP servers:

```powershell
.\tests\desktop-host-smoke.ps1
```

For a faster Docker-only pass:

```powershell
.\tests\mcp-smoke.ps1 -SkipBuild
```

The Docker smoke test restarts the containers, verifies `/healthz`, checks SSE, lists MCP tools, and calls representative tools. Prometheus, GitLab, Jira, and Loki are checked against local mock HTTP APIs. PostgreSQL and SQL Server live DB checks are covered by the fixture scripts in `tests/`.

## LIG AI MCP

`mcp-manager` is a small CLI that starts, stops, checks, and logs both Docker MCP servers and Windows-host MCP servers from one place.

Development use:

```powershell
.\mcp-manager\scripts\run.ps1 list all
.\mcp-manager\scripts\run.ps1 start all
.\mcp-manager\scripts\run.ps1 status all
.\mcp-manager\scripts\run.ps1 stop all
```

Publish a native Windows manager executable:

```powershell
.\mcp-manager\scripts\publish-win.ps1
```

The publish folder includes `McpManager.exe`, `LIG-AI-MCP.cmd`, `mcp-manager.cmd`, `start-all.cmd`, `stop-all.cmd`, `status.cmd`, and `servers.json`.

The bundle also includes `fonts\NotoSansKR[wght].ttf` and `install-fonts.cmd`. Run `install-fonts.cmd` once to install Noto Sans KR for the current Windows user, then restart the terminal and select that font in Windows Terminal/CMD settings if you want a consistent Korean UI font. The console app cannot reliably force a terminal font by itself.

To start all servers without running the smoke calls:

```powershell
.\scripts\run-all.ps1
```

By default, this mounts the repository at `/workspace` and the Windows `C:\` drive at `/host/c`, then sets `MCP_PATH_MAPPINGS=C:\=/host/c`. That lets MCP clients pass normal Windows paths such as `C:\Users\taewon\Desktop\넥스원\2024 분산스위치 논문.hwp`.

When you want the API-backed servers to connect to real internal services, pass the service endpoints and credentials at startup:

```powershell
.\scripts\run-all.ps1 `
  -PostgresConnectionString "Host=postgres.internal;Port=5432;Database=app;Username=mcp;Password=secret" `
  -MssqlConnectionString "Server=mssql.internal;Database=app;User Id=mcp;Password=secret;TrustServerCertificate=True" `
  -PrometheusBaseUrl "http://prometheus.monitoring.svc:9090" `
  -GitLabBaseUrl "https://gitlab.internal" `
  -GitLabToken "glpat-..." `
  -JiraBaseUrl "https://jira.internal" `
  -JiraBearerToken "..." `
  -LokiBaseUrl "http://loki.monitoring.svc:3100"
```

For air-gapped environments, point these values at internal services, internal DNS names, or local mock/fixture services. If a connection string or API URL is omitted, the corresponding server still starts, but tools that need that backend may return configuration errors.

## Path Mapping

For Linux containers to accept Windows host paths from MCP clients, host drives must be mounted with Docker and listed in `MCP_PATH_MAPPINGS`. The provided helper handles every ready Windows drive and publishes the port on localhost only:

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-filesystem -Port 42181
```

The same mapping pattern is supported by path-based servers such as Office, filesystem, Git, shell, .NET, HWP, and Kubernetes. For servers with `mountHostDrives` enabled, MCP Manager automatically mounts every ready Windows drive (C:, D:, E:, and so on) at `/host/drives/<letter>` and configures the path mappings. In native Windows processes, `MCP_ALLOWED_DIRS=*` means every currently connected drive root. Operating-system account permissions and Docker Desktop drive-sharing policy still apply.

## Kubernetes Deployment

Kubernetes manifests are provided for the MCP servers that can reasonably run as Linux Kubernetes workloads:

- Included: `mcp-filesystem`, `mcp-git`, `mcp-dotnet`, `mcp-kubernetes`, `mcp-prometheus`, `mcp-postgresql`, `mcp-gitlab`, `mcp-jira`, `mcp-loki`, `mcp-confluence`
- Excluded in this phase: `mcp-office`, `mcp-shell`, `mcp-hwp`, `mcp-mssql`, `mcp-docker`

Each included server has a `k8s/` folder with namespace, Deployment, Service, and any required ConfigMap/PVC/RBAC files. Apply a server with:

```powershell
kubectl apply -f .\<server>\k8s\
```

The default namespace is `mcp-servers`, container port is `8080`, services are `ClusterIP`, and probes use `GET /healthz`. File/project/repo servers use `/workspace` backed by a PVC instead of broad host-path mounts.
Servers that connect to external systems, such as PostgreSQL, GitLab, Jira, and Loki, include example Secret manifests. Edit those values or create equivalent Secrets before using the read/write API tools against real services.

Air-gapped clusters need the images loaded into the cluster runtime or pushed to an internal registry. The manifests use `local/<server>:latest` by default. For single-node Docker-based clusters this may work after `docker load`; for containerd or multi-node clusters, import the image into each node runtime or rewrite the image reference to a private registry path.

`mcp-docker` is excluded from the default Kubernetes manifests. It requires access to a Docker daemon socket, which is unavailable in many containerd-based clusters and is high-privilege when host-mounted.

`mcp-rhapsody` is also excluded from Kubernetes and Linux Docker because Rhapsody automation depends on a Windows installation, user session, license, COM automation, and local CLI tools.

`mcp-matlab`, `mcp-autocad`, and `mcp-solidworks` are excluded for the same desktop-automation reason: they require installed Windows desktop applications, user/session context, licenses, and COM or local CLI automation.

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
- `mcp-postgresql/README.md`
- `mcp-gitlab/README.md`
- `mcp-jira/README.md`
- `mcp-loki/README.md`
- `mcp-confluence/README.md`
- `mcp-rhapsody/README.md`
- `mcp-matlab/README.md`
- `mcp-autocad/README.md`
- `mcp-solidworks/README.md`

