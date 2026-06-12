# mcp-rhapsody

Korean version: [README.ko.md](README.ko.md)

Windows-host MCP server for IBM Engineering Systems Design Rhapsody automation. It is intentionally not a Docker/Kubernetes server because Rhapsody automation normally depends on a Windows installation, user session, license, COM automation, and local CLI tools.

## Run

Development run:

```powershell
.\mcp-rhapsody\scripts\run-dev.ps1
```

Publish an airgap Windows folder:

```powershell
.\mcp-rhapsody\scripts\publish-win.ps1
```

Run on the target Windows machine:

```powershell
.\run.ps1
```

Connections:

- HTTP: `http://localhost:8094/mcp`
- SSE: `http://localhost:8094/sse`
- Health: `http://localhost:8094/healthz`

## Configuration

Copy `config/rhapsody.env.example` to `config/rhapsody.env` for development, or edit `rhapsody.env` in the published folder.

| Variable | Purpose |
| --- | --- |
| `RHAPSODY_INSTALL_DIR` | Optional explicit Rhapsody install directory. |
| `RHAPSODY_EXE_PATH` | Optional explicit Rhapsody executable path. |
| `RHAPSODY_CLI_PATH` | Optional explicit CLI path. |
| `RHAPSODY_COM_PROGID` | Optional explicit COM ProgID. |
| `MCP_ALLOWED_DIRS` | Allowed Windows roots for project/model files. |
| `MCP_ENABLE_RHAPSODY_WRITES` | Set `false` to block future write tools. |
| `MCP_ENABLE_RHAPSODY_CLI` | Set `false` to block raw CLI execution. |

## COM/CLI Launch Policy

`config`, `detect_installations`, and `/healthz` are safe inspection paths and do not start Rhapsody. They only check COM registration, active Rhapsody availability, and install/CLI candidates.

COM tools such as `open_project`, `current_project`, list/save/create/search tools require a real Rhapsody COM session. Under the current policy, they may start Rhapsody when no active session exists. `run_rhapsody_cli` starts the configured CLI only when explicitly called.

## Tools

| Tool | What it does |
| --- | --- |
| `config` | Returns configured values and detected Rhapsody integration status. |
| `detect_installations` | Searches common install paths, PATH, and COM hints. |
| `inspect_project_file` | Reads basic metadata from `.rpy`, `.rpyx`, `.sbs`, `.cls`, or `.omd` files without opening Rhapsody. |
| `run_rhapsody_cli` | Runs the configured Rhapsody CLI with raw arguments. |
| `open_project` | Opens a Rhapsody project through COM Automation. |
| `current_project` | Returns the active project. |
| `save_project` | Saves the active project. |
| `list_packages` | Lists packages in the active project. |
| `list_classes` | Lists classes in the active project. |
| `list_interfaces` | Lists interfaces and interface blocks. |
| `list_statecharts` | Lists statecharts and state machines. |
| `get_element` | Finds an element by name/full path and metaclass. |
| `search_elements` | Searches elements by name/full path. |
| `create_package` | Creates a package. |
| `create_class` | Creates a class. |
| `create_interface` | Creates an interface. |
| `set_element_property` | Sets an element property value. |
| `set_element_tag` | Sets an element tag value. |

## Notes

The server starts even when Rhapsody is not installed. In that case `config` reports missing COM/CLI detection, file inspection still works, and COM/CLI tools return a clear configuration error.

COM tools must run in a Windows user session where Rhapsody is installed. The server uses late-bound calls based on the IBM Rhapsody API object model, including calls such as `activeProject`, `openProject`, `findNestedElement`, `getNestedElements`, `addClass`, `addPackage`, and `save`.

## Tests

On a development PC without Rhapsody, the smoke test verifies server startup, MCP initialization, tool registration, and `config`.

```powershell
.\tests\rhapsody-smoke.ps1
```

On a Windows PC with Rhapsody installed, pass a real project file to run COM read smoke calls.

```powershell
.\tests\rhapsody-smoke.ps1 -RhapsodyProjectPath "C:\path\model.rpyx"
```

To verify write tools as well, explicitly enable write smoke. This creates a smoke package/class and saves the project.

```powershell
.\tests\rhapsody-smoke.ps1 -RhapsodyProjectPath "C:\path\model.rpyx" -RunWriteSmoke
```
