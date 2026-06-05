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

## Run

```powershell
docker run --rm -p 8081:8080 -v ${PWD}:/workspace local/mcp-filesystem
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

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `MCP_ENABLE_WRITES` | `true` in Dockerfile | Optional compatibility switch; set `false` to block write/copy/move/delete. |
