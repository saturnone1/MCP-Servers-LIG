# mcp-git

Korean version: [README.ko.md](README.ko.md)

C# remote MCP Git server over Streamable HTTP. It wraps the `git` CLI inside the container.

## Lineage

- Upstream / reference behavior: official/reference Git MCP server lineage from `modelcontextprotocol/servers`.
- Strategy: C# wrapper around the container's `git` CLI instead of porting Python source.
- Runtime requirement: target repositories must be mounted into the container.
- Trusted-local Docker defaults enable mutating Git operations.

## Build

```powershell
docker build -t local/mcp-git .
```

## Run

```powershell
docker run --rm -p 8082:8080 -v ${PWD}:/workspace local/mcp-git
```

Connect MCP clients with Streamable HTTP at `http://localhost:8082/mcp` or legacy SSE at `http://localhost:8082/sse`. Trusted-local images enable mutating git tools by default and allow `/` inside the container unless `MCP_ALLOWED_DIRS` overrides it.

## Tools

| Tool | What it does |
| --- | --- |
| `status` | Runs `git status --short --branch`. |
| `log` | Returns recent commits. |
| `diff` | Shows unstaged, staged, or refspec diffs. |
| `show` | Shows a Git object or commit. |
| `branch_list` | Lists local and remote branches. |
| `blame` | Runs `git blame` for a file or line range. |
| `grep` | Searches tracked content with `git grep`. |
| `init` | Runs `git init`. |
| `add` | Runs `git add` for paths. |
| `commit` | Runs `git commit`. |
| `checkout` | Runs `git checkout`, optionally creating a branch. |

## API Reference

Most tools return `{ "exitCode": number, "stdout": string, "stderr": string }` from the underlying `git` process.

| Tool | Arguments | Git command |
| --- | --- | --- |
| `status` | `repositoryPath` string = `.` | `git status --short --branch` |
| `log` | `repositoryPath` string = `.`, `maxCount` int = `20` | `git log` |
| `diff` | `repositoryPath` string = `.`, `refspec` string? = `null`, `staged` bool = `false` | `git diff` |
| `show` | `repositoryPath` string, `revision` string | `git show` |
| `branch_list` | `repositoryPath` string = `.` | `git branch --all` |
| `blame` | `repositoryPath` string, `filePath` string, `startLine` int? = `null`, `endLine` int? = `null` | `git blame` |
| `grep` | `repositoryPath` string, `pattern` string, `maxMatches` int = `100` | `git grep` |
| `init` | `repositoryPath` string | `git init` |
| `add` | `repositoryPath` string, `paths` string array | `git add` |
| `commit` | `repositoryPath` string, `message` string | `git commit -m` |
| `checkout` | `repositoryPath` string, `target` string, `createBranch` bool = `false` | `git checkout` or `git checkout -b` |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Allowed container roots for repository paths. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to mounted Linux container paths. |
| `MCP_ENABLE_GIT_WRITES` | `true` in Dockerfile | Optional compatibility switch; set `false` to block `init/add/commit/checkout`. |
