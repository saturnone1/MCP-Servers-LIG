# mcp-solidworks

Korean version: [README.ko.md](README.ko.md)

Windows-host MCP server for SolidWorks automation. No Dassault/SolidWorks-official MCP server was found, so this implementation follows open-source SolidWorks MCP patterns based on the SolidWorks COM API.

## Lineage

- Open-source reference pattern: SolidWorks MCP servers commonly wrap the SolidWorks COM API, for example [`vespo92/SolidworksMCP-TS`](https://github.com/vespo92/SolidworksMCP-TS), [`tylerstoltz/SW_MCP`](https://github.com/tylerstoltz/SW_MCP), and [`eyfel/mcp-server-solidworks`](https://github.com/eyfel/mcp-server-solidworks).
- Local implementation: C#/.NET ASP.NET Core MCP server using late-bound COM Automation.
- Runtime model: Windows user session, SolidWorks installation, local license, COM Automation.

This server is not a Docker/Kubernetes target because SolidWorks automation depends on desktop Windows, GUI/session state, licensing, and COM.

## Run

```powershell
.\mcp-solidworks\scripts\run-dev.ps1
```

Publish for an air-gapped Windows PC:

```powershell
.\mcp-solidworks\scripts\publish-win.ps1
```

The publish folder includes `McpSolidWorks.exe`, `start.cmd`, `run.ps1`, and `solidworks.env`. You can double-click `start.cmd`, run `.\run.ps1`, or run `.\McpSolidWorks.exe` directly after setting environment variables.

Connections:

- HTTP: `http://localhost:8097/mcp`
- SSE: `http://localhost:8097/sse`
- Health: `http://localhost:8097/healthz`

## Configuration

| Variable | Description |
| --- | --- |
| `SOLIDWORKS_EXE_PATH` | Optional `SLDWORKS.exe` path hint. |
| `SOLIDWORKS_COM_PROGID` | COM ProgID, usually `SldWorks.Application`. |
| `MCP_ALLOWED_DIRS` | Semicolon-separated Windows roots allowed for CAD files and exports. |
| `MCP_ENABLE_SOLIDWORKS_WRITES` | Set `false` to block modification/export tools. |

## COM Launch Policy

`config`, `detect_installations`, and `/healthz` are safe inspection paths and do not start SolidWorks. They only check COM registration and whether a SolidWorks session is already active.

Tools that operate on documents, such as `open_document`, `active_document`, list/save/export tools, require a real SolidWorks COM session. Under the current policy, they may start SolidWorks when no active session exists.

## Tools

| Tool | Capability |
| --- | --- |
| `config` | Returns configuration and COM detection status. |
| `detect_installations` | Finds COM and executable hints. |
| `open_document` | Opens part, assembly, or drawing files. |
| `active_document` | Returns active document info. |
| `list_features` | Lists top-level features. |
| `list_components` | Lists assembly components. |
| `list_configurations` | Lists configurations. |
| `list_equations` | Lists equations. |
| `list_custom_properties` | Lists custom properties. |
| `set_custom_property` | Sets or adds a custom property. |
| `get_mass_properties` | Returns mass/volume/surface area when available. |
| `rebuild_model` | Rebuilds the active model. |
| `save_document` | Saves the active document. |
| `export_document` | Exports through SolidWorks `SaveAs` to formats such as STEP/STL/PDF when supported. |
| `export_step` | Exports as STEP. |
| `export_stl` | Exports as STL. |
| `export_pdf` | Exports as PDF. |
| `close_active_document` | Closes the active document. |
