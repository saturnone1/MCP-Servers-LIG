# mcp-office

Korean version: [README.ko.md](README.ko.md)

C# remote MCP wrapper for OfficeCLI over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: `iOfficeAI/OfficeCLI`
- Strategy: bundle the Linux x64 OfficeCLI release into the Docker image and expose it as MCP tools.
- Extra compatibility: install `antiword` for legacy `.doc` text extraction.
- Runtime target: headless and Office-free. Microsoft Office does not need to be installed in the container.

## Build

```powershell
docker build -t local/mcp-office .
```

The Dockerfile downloads the Linux x64 OfficeCLI release during build and embeds it in the final image, so runtime does not need internet access.

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-office:latest` as `airgap/local-mcp-office.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 8080:8080 -v ${PWD}:/workspace local/mcp-office
```

Connect MCP clients with Streamable HTTP at `http://localhost:8080/mcp` or legacy SSE at `http://localhost:8080/sse`. Trusted-local images enable document creation, batch edits, render/export, and raw OfficeCLI calls by default.

## Tools

| Tool | What it does |
| --- | --- |
| `version` | Returns `officecli --version`. |
| `inspect_document` | Runs OfficeCLI inspection/dump modes for an Office document. |
| `extract_text` | Extracts readable text from `.doc`, `.docx`, `.xlsx`, or `.pptx`. |
| `create_document` | Creates an Office document using OfficeCLI. |
| `apply_batch` | Applies an OfficeCLI batch JSON file to a document. |
| `render_document` | Renders or exports a document to an output path. |
| `run_office_cli` | Runs raw OfficeCLI arguments for advanced operations. |

## API Reference

All tools return a command-style object: `{ "exitCode": number, "stdout": string, "stderr": string }`.

| Tool | Arguments | Notes |
| --- | --- | --- |
| `version` | none | Returns the bundled OfficeCLI version. |
| `inspect_document` | `path` string, `mode` string = `text` | Runs OfficeCLI inspection for `.docx`, `.xlsx`, `.pptx`, and supported OfficeCLI formats. |
| `extract_text` | `path` string, `maxLines` int = `200` | Uses OfficeCLI for modern Office files and `antiword` for legacy `.doc`. |
| `create_document` | `path` string | Creates a document at the mapped path. |
| `apply_batch` | `documentPath` string, `batchJsonPath` string | Applies an OfficeCLI batch JSON file. |
| `render_document` | `documentPath` string, `outputPath` string | Renders or exports to the requested output path. |
| `run_office_cli` | `args` string array, `timeoutMs` int = `120000` | Advanced escape hatch for raw OfficeCLI arguments. |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots for file paths. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `MCP_ENABLE_OFFICE_WRITES` | `true` in Dockerfile | Optional compatibility switch; set `false` to block mutating Office tools. |
| `OFFICECLI_PATH` | bundled OfficeCLI path | Override OfficeCLI executable path. |
| `ANTIWORD_PATH` | `antiword` | Override legacy `.doc` extractor. |

## Notes

The `extract_text` tool reads modern Office files through OfficeCLI and legacy `.doc` files through `antiword`.

## Kubernetes

No Kubernetes manifests are provided for `mcp-office` in this phase. It is excluded from the Linux Kubernetes set because it is primarily a desktop/document-conversion workload and was explicitly grouped with local/Windows-oriented servers for this review.
