# mcp-plantuml

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server that renders PlantUML diagrams over Streamable HTTP and legacy SSE.

## Lineage

- Reference surface: community PlantUML MCP servers such as [infobip/plantuml-mcp-server](https://github.com/infobip/plantuml-mcp-server) (TypeScript, MIT).
- Difference that motivated a local implementation: those servers encode the diagram and hand the work to `https://www.plantuml.com/plantuml`. This server renders **locally** with `plantuml.jar` or the `plantuml` CLI, so diagram source never leaves the machine and the server works in an air-gapped network. A remote PlantUML server stays available as an explicit fallback through `PLANTUML_SERVER_URL`.
- Runtime requirement: a local renderer (Java plus `plantuml.jar`, or the `plantuml` CLI) or a reachable PlantUML server. The Docker image installs the Debian `plantuml` package, so the container renders offline out of the box. The Windows bundle ships its own `tools/plantuml.jar`, so only a Java runtime is needed there.

## Renderer resolution

The server picks the first renderer that is actually usable, and reports the result from `config` and `/healthz`:

1. `PLANTUML_JAR_PATH` when the jar exists and `JAVA_PATH` resolves — runs `java -Djava.awt.headless=true -jar <jar> -pipe`.
2. `PLANTUML_PATH` when the CLI is on `PATH` — runs `plantuml -pipe`.
3. `PLANTUML_SERVER_URL` when set — encodes the diagram and fetches it over HTTP.
4. Otherwise every render tool fails with an explicit message naming all three options.

Only options 1 and 2 are offline capable. `config` reports this as `offlineCapable`.

## Bundled jar

`scripts/download-plantuml.ps1` fetches a PlantUML jar into `vendor/plantuml/plantuml.jar`, verifies its SHA256 against the release digest, and records the provenance in `vendor/plantuml/README.txt`. `publish-mcp-bundle.ps1` copies it into the bundle as `tools/plantuml.jar` and the bundle config points `PLANTUML_JAR_PATH` at it, so the Windows bundle renders without a separate PlantUML install.

PlantUML publishes the same engine under several licenses. The download script defaults to the **MIT** edition rather than the GPL `plantuml.jar`, because the bundle is redistributed inside a commercial installer:

```powershell
.\scripts\download-plantuml.ps1                     # MIT edition, latest release
.\scripts\download-plantuml.ps1 -Edition asl        # Apache-2.0
.\scripts\download-plantuml.ps1 -Version v1.2026.6  # pin a release
```

The jar is not committed; `vendor/` is git-ignored like the OfficeCLI and MATLAB vendor folders.

**The jar still needs Java.** The bundle does not ship a JRE, so the target PC needs one on `PATH` or in `JAVA_PATH`. Without Java the server falls back to `PLANTUML_SERVER_URL` if configured, and otherwise reports `renderer: none`.

## Build

```powershell
docker build -t local/mcp-plantuml .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-plantuml:latest` as `airgap/local-mcp-plantuml.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it. The image carries its own renderer, so no PlantUML server is needed on the air-gapped side.

## Run

```powershell
docker run --rm -p 127.0.0.1:8100:8080 `
  -v "${PWD}:/workspace" `
  -e "MCP_ALLOWED_DIRS=/workspace" `
  -e "MCP_PATH_MAPPINGS=${PWD}=/workspace" `
  local/mcp-plantuml
```

Connect MCP clients with Streamable HTTP at `http://localhost:8100/mcp` or legacy SSE at `http://localhost:8100/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `config` | Reports the resolved renderer, the configured paths, and whether rendering works offline. |
| `list_formats` | Lists the supported formats and whether each returns text or base64. |
| `render_diagram` | Renders PlantUML source and returns the diagram inline. |
| `render_source_file` | Reads a `.puml` file and renders it without writing anything. |
| `render_to_file` | Renders source and writes the diagram to an explicit output path. |
| `render_file_to_directory` | Renders a `.puml` file next to itself or into an output directory. |
| `check_syntax` | Validates source without producing a diagram. |
| `read_source` | Reads a PlantUML source file. |
| `encode_url` | Encodes source into the compressed PlantUML form and builds a server URL. |
| `decode_url` | Decodes an encoding or a full PlantUML URL back into source. |

Formats are `svg`, `png`, `txt`, `utxt`, `eps`, and `latex`. Text formats come back as text; `png` and `eps` come back base64 encoded with the byte count reported alongside.

`encode_url` and `decode_url` implement PlantUML's own deflate plus custom base64 alphabet, so the values interoperate with any PlantUML server.

## Path and write safety

`read_source`, `render_source_file`, `render_to_file`, and `render_file_to_directory` resolve every path through `MCP_ALLOWED_DIRS` and `MCP_PATH_MAPPINGS`, matching the other filesystem-touching servers in this repository. The three tools that write are additionally gated by `MCP_ENABLE_PLANTUML_WRITES`.

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `PLANTUML_JAR_PATH` | empty in Docker, `tools/plantuml.jar` in the bundle | Path to `plantuml.jar`. Preferred renderer when Java is available. |
| `JAVA_PATH` | `java` | Java executable used with the jar. |
| `PLANTUML_PATH` | `plantuml` | PlantUML CLI used when no jar is configured. |
| `PLANTUML_SERVER_URL` | empty | Remote PlantUML server, used only when no local renderer exists. |
| `PLANTUML_INCLUDE_PATH` | empty | Search path for `!include` directives and diagram libraries. |
| `MCP_ALLOWED_DIRS` | `/` in Dockerfile, `*` in the bundle | Roots that file tools may touch. |
| `MCP_PATH_MAPPINGS` | empty | Host to container path mappings, for example `C:\work=/workspace`. |
| `MCP_ENABLE_PLANTUML_WRITES` | `true` in Dockerfile | Set `false` to block the tools that write diagrams to disk. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The server needs no external endpoint because the image renders locally.
