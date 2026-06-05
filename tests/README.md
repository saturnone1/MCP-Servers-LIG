# MCP Smoke Tests

This folder contains smoke tests for the six Docker MCP servers.

## Run

```powershell
.\tests\mcp-smoke.ps1
```

The script builds images, restarts the six containers, verifies `/healthz`, verifies legacy SSE at `/sse`, lists MCP tools over Streamable HTTP, and calls one representative tool per server.

## Host Path Mapping

Windows host paths must be mounted into Docker before Linux containers can read them. The script mounts this folder when it exists:

```text
C:\Users\taewon\Desktop\가상화 -> /virtualization
```

It also sets:

```text
MCP_PATH_MAPPINGS=C:\Users\taewon\Desktop\가상화=/virtualization
```

That lets clients pass the original Windows path while the server transparently calls the mounted Linux path.

## MSSQL

Without a SQL connection string, the MSSQL smoke test verifies that the server no longer blocks writes by policy and reports the expected missing connection string error. To run a real SQL query:

```powershell
.\tests\mcp-smoke.ps1 -MssqlConnectionString "Server=host.docker.internal;Database=master;User Id=sa;Password=...;TrustServerCertificate=True"
```
