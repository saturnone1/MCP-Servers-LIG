# mcp-shell

Korean version: [README.ko.md](README.ko.md)

C# remote MCP shell server over Streamable HTTP.

## Lineage

- Upstream / porting source: none.
- Strategy: direct C# implementation with `ProcessStartInfo`.
- Purpose: trusted local command execution inside the Docker container.
- Safety controls remain available through environment variables, but Docker defaults allow execution for local testing.

## Build

```powershell
docker build -t local/mcp-shell .
```

## Run

```powershell
docker run --rm -p 8083:8080 -v ${PWD}:/workspace local/mcp-shell
```

Connect MCP clients with Streamable HTTP at `http://localhost:8083/mcp` or legacy SSE at `http://localhost:8083/sse`. Trusted-local images enable shell execution by default. Use `MCP_SHELL_ALLOWED_COMMANDS` and `MCP_SHELL_ALLOWED_ENV` for optional allowlists.

## Tools

| Tool | What it does |
| --- | --- |
| `run_command` | Runs a command with arguments, working directory, timeout, max output size, and optional environment variables. |

## API Reference

| Tool | Arguments | Returns |
| --- | --- | --- |
| `run_command` | `command` string, `args` string array = `[]`, `workingDirectory` string = `/workspace`, `timeoutMs` int = `30000`, `maxOutputBytes` int = `1048576`, `environment` object? = `null` | `{ "exitCode": number, "stdout": string, "stderr": string }` |

`workingDirectory` can be a mapped Windows host path. `environment` is filtered by `MCP_SHELL_ALLOWED_ENV` when that allowlist is set.

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ENABLE_SHELL` | `true` in Dockerfile | Optional compatibility switch; set `false` to block shell execution. |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots for working directories. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `MCP_SHELL_ALLOWED_COMMANDS` | empty | Optional command allowlist. Empty means any command. |
| `MCP_SHELL_ALLOWED_ENV` | empty | Optional environment-variable allowlist. Empty means no custom env vars are passed unless the server code allows them. |
