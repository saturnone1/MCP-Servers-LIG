# mcp-autocad

Korean version: [README.ko.md](README.ko.md)

Windows-host MCP server for AutoCAD automation. No broadly adopted Autodesk-official AutoCAD desktop MCP server was found, so this implementation follows the practical pattern used by open-source AutoCAD MCP projects: local Windows session plus AutoCAD COM Automation.

## Lineage

- Official ecosystem reference: Autodesk Platform Services has an MCP server at [`autodesk-platform-services/aps-mcp-server-nodejs`](https://github.com/autodesk-platform-services/aps-mcp-server-nodejs), but it is APS/API-oriented rather than AutoCAD desktop COM.
- Open-source desktop pattern references include CAD/AutoCAD MCP projects such as [`daobataotie/CAD-MCP`](https://github.com/daobataotie/CAD-MCP) and [`zh19980811/Easy-MCP-AutoCad`](https://github.com/zh19980811/Easy-MCP-AutoCad).
- Local implementation: C#/.NET ASP.NET Core MCP server using AutoCAD COM Automation.
- Runtime model: Windows user session, AutoCAD installation, local license, COM Automation.

This server is not a Docker/Kubernetes target because AutoCAD desktop automation depends on Windows COM, GUI/session state, and licensing.

## Run

```powershell
.\mcp-autocad\scripts\run-dev.ps1
```

Publish for an air-gapped Windows PC:

```powershell
.\mcp-autocad\scripts\publish-win.ps1
```

The publish folder includes `McpAutoCad.exe`, `start.cmd`, `run.ps1`, and `autocad.env`. You can double-click `start.cmd`, run `.\run.ps1`, or run `.\McpAutoCad.exe` directly after setting environment variables.

Connections:

- HTTP: `http://localhost:8096/mcp`
- SSE: `http://localhost:8096/sse`
- Health: `http://localhost:8096/healthz`

## Configuration

| Variable | Description |
| --- | --- |
| `AUTOCAD_EXE_PATH` | Optional `acad.exe` path hint. |
| `AUTOCAD_COM_PROGID` | COM ProgID, usually `AutoCAD.Application`. |
| `MCP_ALLOWED_DIRS` | Semicolon-separated Windows roots allowed for drawings. |
| `MCP_ENABLE_AUTOCAD_WRITES` | Set `false` to block drawing modification tools. |

## Tools

| Tool | Capability |
| --- | --- |
| `config` | Returns configuration and COM detection status. |
| `detect_installations` | Finds COM and executable hints. |
| `open_drawing` | Opens a DWG/DXF through COM. |
| `active_drawing` | Returns active drawing info. |
| `list_layers` | Lists layers in the active drawing. |
| `list_model_space_entities` | Lists model-space entities. |
| `list_blocks` | Lists block definitions. |
| `list_block_references` | Lists inserted block references. |
| `list_texts` | Lists text and mtext entities. |
| `list_dimensions` | Lists dimension entities. |
| `list_curves` | Lists line and polyline entities. |
| `run_command` | Sends an AutoCAD command string. |
| `create_layer` | Creates a layer. |
| `add_line` | Adds a line to model space. |
| `add_circle` | Adds a circle to model space. |
| `add_text` | Adds single-line text to model space. |
| `save_drawing` | Saves the active drawing. |
| `export_drawing` | Exports the active drawing using AutoCAD `Export`. |
| `save_as_drawing` | Saves a copy to a target path. |
