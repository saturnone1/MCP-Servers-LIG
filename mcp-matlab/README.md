# mcp-matlab

Korean version: [README.ko.md](README.ko.md)

Windows-host MCP server for MATLAB and Simulink automation. MATLAB has an official MathWorks MCP Core Server, so this implementation is intentionally compatible with that direction while also exposing local C# tools for `matlab -batch` and MATLAB COM Automation.

## Lineage

- Official upstream reference: [`matlab/matlab-mcp-core-server`](https://github.com/matlab/matlab-mcp-core-server)
- Local implementation: C#/.NET ASP.NET Core MCP server with Streamable HTTP and legacy SSE
- Runtime model: Windows user session, MATLAB installation, local license, optional COM Automation

This server is not a Docker/Kubernetes target. MATLAB GUI, license, COM, and Simulink workflows are usually host-session dependent.

## Run

Development:

```powershell
.\mcp-matlab\scripts\run-dev.ps1
```

Publish for an air-gapped Windows PC:

```powershell
.\mcp-matlab\scripts\publish-win.ps1
```

To bundle the official MathWorks MCP server into that publish folder, download it on an internet-connected machine first:

```powershell
.\mcp-matlab\scripts\download-official-mcp.ps1
.\mcp-matlab\scripts\publish-win.ps1
```

The download script stores the official binary under `mcp-matlab/vendor/official/`. `publish-win.ps1` copies that folder to `publish/official/`, and `run.ps1` auto-detects `official/matlab-mcp*.exe` when `MATLAB_MCP_CORE_SERVER_PATH` is not set.

The publish folder includes:

- `McpMatlab.exe`: native Windows executable
- `start.cmd`: double-click launcher
- `run.ps1`: PowerShell launcher
- `matlab.env`: editable configuration
- `official/`: optional bundled MathWorks MCP binary

Run from the published folder:

```powershell
.\run.ps1
```

You can also double-click `start.cmd` or run `.\McpMatlab.exe` directly after setting environment variables.

Connections:

- HTTP: `http://localhost:8095/mcp`
- SSE: `http://localhost:8095/sse`
- Health: `http://localhost:8095/healthz`

## Configuration

Copy `config/matlab.env.example` to `config/matlab.env` for development, or edit `matlab.env` in the published folder.

| Variable | Description |
| --- | --- |
| `MATLAB_ROOT` | MATLAB root directory. |
| `MATLAB_EXE_PATH` | Explicit `matlab.exe` path. |
| `MATLAB_COM_PROGID` | COM ProgID, usually `Matlab.Application`. |
| `MATLAB_MCP_CORE_SERVER_PATH` | Optional path to the official MathWorks MCP Core Server executable/script. |
| `MATLAB_MCP_CORE_SERVER_ARGS` | Optional arguments passed to the official MathWorks MCP Core Server. |
| `MCP_ALLOWED_DIRS` | Semicolon-separated Windows roots allowed for script files. |
| `MCP_ENABLE_MATLAB_WRITES` | Set `false` to block future write-oriented tools. |

## Tools

| Tool | Capability |
| --- | --- |
| `config` | Returns configuration, detection, and official MCP path status. |
| `detect_installations` | Finds MATLAB from env vars, PATH, and common install folders. |
| `run_batch` | Runs `matlab -batch "<command>"`. |
| `run_script` | Runs a `.m` file with `matlab -batch run('path')`. |
| `eval_command` | Evaluates code through MATLAB COM Automation. |
| `list_workspace` | Runs `whos` through COM and returns the workspace summary. |
| `official_mcp_initialize` | Starts the official MathWorks MCP server over stdio and returns its initialize response. |
| `official_mcp_tools_list` | Lists tools from the official MathWorks MCP server. |
| `official_mcp_tool_call` | Calls a tool from the official MathWorks MCP server through the bridge. |
| `official_mcp_raw_request` | Sends a raw JSON-RPC request to the official MathWorks MCP server after initialize. |
| `simulink_load_system` | Loads a Simulink model/system. |
| `simulink_find_system` | Runs `find_system` and prints JSON output. |
| `get_param` | Reads a MATLAB/Simulink parameter. |
| `set_param` | Sets a MATLAB/Simulink parameter. |
| `simulink_simulate` | Runs `sim`. |
| `simulink_build` | Runs `slbuild`. |

## Official MATLAB MCP Bridge

Set `MATLAB_MCP_CORE_SERVER_PATH` to the official MathWorks MCP server binary, or place the binary in the published `official/` folder. This server then exposes the official server through generic bridge tools:

```powershell
MATLAB_MCP_CORE_SERVER_PATH=C:\tools\matlab-mcp-core-server-windows-x64.exe
```

Use `official_mcp_tools_list` to discover upstream tools, then `official_mcp_tool_call` with the upstream tool name and arguments. The bridge starts a short-lived stdio MCP child process per call, initializes it, forwards the request, and returns the official JSON-RPC response.

For air-gap delivery, copy the published folder as a unit. It contains this C# HTTP/SSE MCP server, the optional official MathWorks MCP binary under `official/`, and `matlab.env`.

## Notes

If MATLAB is not installed, the server still starts and `config` reports the missing executable/COM state. Tools that require MATLAB return clear configuration errors.
