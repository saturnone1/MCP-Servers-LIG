# mcp-confluence

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Confluence Data Center/Server REST API operations over Streamable HTTP and legacy SSE.

## Compatibility

This server intentionally uses Confluence Data Center/Server REST API v1 paths under `/rest/api/...`, not Confluence Cloud REST API v2. The selected endpoints cover the long-lived Server/Data Center REST surface used by Confluence Server 5.5+, Data Center 5.6+, 8.5 LTS, 9.2 LTS/9.2.9, and current 10.x Data Center releases:

- `/rest/api/user/current`
- `/rest/api/settings/systemInfo`
- `/rest/troubleshooting/1.0/pre-upgrade/info`
- `/rest/api/space`
- `/rest/api/content/search`
- `/rest/api/content/{id}`
- `/rest/api/content/{id}/child/page`
- `/rest/api/content`

## Build

```powershell
docker build -t local/mcp-confluence .
```

## Run

```powershell
docker run --rm -p 127.0.0.1:42198:8080 `
  -e "CONFLUENCE_BASE_URL=https://confluence.example.local" `
  -e "CONFLUENCE_BEARER_TOKEN=<token>" `
  local/mcp-confluence
```

Connect MCP clients with Streamable HTTP at `http://localhost:42198/mcp` or legacy SSE at `http://localhost:42198/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `config` | Returns Confluence base URL, auth configuration status, and compatibility note. |
| `server_info` | Gets server/version information when the target exposes it. |
| `current_user` | Gets the current Confluence user. |
| `list_spaces` | Lists spaces. |
| `get_space` | Gets one space by key. |
| `list_content` | Lists content with classic `/rest/api/content` query parameters. |
| `search_content` | Searches content using CQL. |
| `get_content` | Gets one content item by id. |
| `list_child_pages` | Lists child pages below a content item. |
| `create_page` | Creates a page with storage-format body. |
| `update_page` | Updates a page using the next version number. |
| `delete_content` | Deletes or trashes a content item. |
| `update_page_auto` | Updates a page without managing versions manually. Fetches current version/space/title and retries once on 409. |
| `append_to_page` | Appends a storage-format fragment to the end of a page without regenerating the body. |
| `prepend_to_page` | Prepends a storage-format fragment to the beginning of a page without regenerating the body. |
| `replace_section` | Replaces one section under a matched heading (up to the next same-or-higher-level heading). Fails on ambiguous matches unless `occurrenceIndex` is given. |
| `append_to_section` | Appends a fragment at the end of a section (before the next boundary heading). |
| `insert_after_heading` | Inserts a fragment immediately after a matched heading. |
| `find_replace_text` | Replaces a substring in the page body, verifying occurrence count first to avoid unintended matches. |
| `get_section` | Reads a single section by heading text without transferring the full page. |
| `preview_storage` | Validates a storage-format fragment for XHTML well-formedness before committing. |

List/search tools default to the maximum request size of 100 and expose `start` for unrestricted pagination.

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `CONFLUENCE_BASE_URL` | `http://confluence.local` | Confluence base URL, including context path if needed, for example `https://host/confluence`. |
| `CONFLUENCE_BEARER_TOKEN` | empty | Bearer token authentication. |
| `CONFLUENCE_PAT` | empty | Alias for bearer token authentication, useful for Data Center personal access tokens. |
| `CONFLUENCE_USERNAME` | empty | Username for Basic authentication. |
| `CONFLUENCE_API_TOKEN` | empty | API token or password for Basic authentication. |
| `CONFLUENCE_PASSWORD` | empty | Fallback Basic auth secret when `CONFLUENCE_API_TOKEN` is not set. |
| `CONFLUENCE_COOKIE` | empty | Raw Cookie header for SSO/session-proxy deployments, for example `JSESSIONID=...`. |
| `MCP_ENABLE_CONFLUENCE_WRITES` | `true` in Dockerfile | Set `false` to block create/update/delete tools. |

