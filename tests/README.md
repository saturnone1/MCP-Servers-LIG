# MCP Smoke Tests

## `verify-priority.ps1`

Runs the current priority verification sequence:

- Docker MCP smoke with external API mocks
- PostgreSQL disposable fixture smoke
- SQL Server disposable fixture smoke
- Rhapsody Windows-host MCP smoke

MATLAB, AutoCAD, and SolidWorks are covered by `desktop-host-smoke.ps1` because they use separate Windows desktop host ports and are not Docker services.

Run:

```powershell
.\tests\verify-priority.ps1 -SkipBuild -SkipImagePull
```

On a Rhapsody-installed Windows PC, add a real project path to include COM read smoke. Add `-RunRhapsodyWriteSmoke` only when it is safe to modify and save that model.

```powershell
.\tests\verify-priority.ps1 -SkipBuild -SkipImagePull -RhapsodyProjectPath "C:\path\model.rpyx"
.\tests\verify-priority.ps1 -SkipBuild -SkipImagePull -RhapsodyProjectPath "C:\path\model.rpyx" -RunRhapsodyWriteSmoke
```

## `mcp-smoke.ps1`

Starts the Docker MCP servers, checks `/healthz`, verifies legacy SSE, initializes MCP over `/mcp`, lists tools, and calls representative tools.

It also starts local mock HTTP APIs for:

- Prometheus: `query`, `labels`
- GitLab: `list_projects`
- Jira: `list_projects`
- Loki: `labels`, `recent_logs`

Run:

```powershell
.\tests\mcp-smoke.ps1 -SkipBuild
```

## `db-fixture-smoke.ps1`

Starts a disposable PostgreSQL container, creates a smoke table, then runs `mcp-smoke.ps1` with a real `POSTGRES_CONNECTION_STRING` so `mcp-postgresql` executes an actual read query.

Run:

```powershell
.\tests\db-fixture-smoke.ps1
```

Use `-SkipImagePull` after `postgres:16-alpine` is already available locally.

## SQL Server

`mcp-smoke.ps1` accepts `-MssqlConnectionString` and will execute `select 1 as ok` through `mcp-mssql` when provided.

`mssql-fixture-smoke.ps1` starts a disposable SQL Server Developer container, creates a smoke database/table, then runs `mcp-smoke.ps1` with a real `MSSQL_CONNECTION_STRING`.

Run:

```powershell
.\tests\mssql-fixture-smoke.ps1
```

Use `-SkipImagePull` after `mcr.microsoft.com/mssql/server:2022-latest` is already available locally. This test accepts the SQL Server container EULA for the disposable fixture and can take a few minutes on the first run.

## Rhapsody

`rhapsody-smoke.ps1` runs the Windows-host `mcp-rhapsody` server and verifies MCP startup, tool registration, and `config`.

```powershell
.\tests\rhapsody-smoke.ps1
```

On a Rhapsody-installed Windows PC, pass a project path to run actual COM read calls. Add `-RunWriteSmoke` only when it is safe to create a smoke package/class and save the project.

```powershell
.\tests\rhapsody-smoke.ps1 -RhapsodyProjectPath "C:\path\model.rpyx"
.\tests\rhapsody-smoke.ps1 -RhapsodyProjectPath "C:\path\model.rpyx" -RunWriteSmoke
```

## Desktop Host MCP

`desktop-host-smoke.ps1` runs the Windows-host MATLAB, AutoCAD, and SolidWorks MCP servers and verifies `/healthz`, MCP startup, tool registration, and `config`. It also verifies the MATLAB official MCP bridge against `mock-stdio-mcp.ps1`, so `official_mcp_tools_list` and `official_mcp_tool_call` are exercised without requiring MATLAB.

This smoke does not require MATLAB, AutoCAD, or SolidWorks to be installed. Product-specific COM/API tools still require the corresponding desktop application, license, and user session.

```powershell
.\tests\desktop-host-smoke.ps1
```
