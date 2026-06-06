# mcp-git

영어 버전: [README.md](README.md)

컨테이너 내부 `git` CLI를 MCP tool로 제공하는 C# 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 참고 원본: `modelcontextprotocol/servers`의 공식/reference Git MCP 서버 계보
- 구현 방식: Python 소스 포팅 대신 `git` CLI를 호출하는 C# 래퍼로 재구현했습니다.
- 런타임 요구사항: 대상 Git 저장소를 컨테이너에 볼륨으로 마운트해야 합니다.
- trusted-local Docker 기본값: `init/add/commit/checkout` 같은 변경 작업을 허용합니다.

## 빌드

```powershell
docker build -t local/mcp-git .
```

## 실행

```powershell
docker run --rm -p 8082:8080 -v ${PWD}:/workspace local/mcp-git
```

연결 주소:

- Streamable HTTP: `http://localhost:8082/mcp`
- Legacy SSE: `http://localhost:8082/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `status` | `git status --short --branch`를 실행합니다. |
| `log` | 최근 커밋 목록을 반환합니다. |
| `diff` | unstaged, staged, refspec diff를 보여줍니다. |
| `show` | Git object 또는 commit을 보여줍니다. |
| `branch_list` | local/remote branch를 나열합니다. |
| `blame` | 파일 또는 line range에 대해 `git blame`을 실행합니다. |
| `grep` | `git grep`으로 tracked content를 검색합니다. |
| `init` | `git init`을 실행합니다. |
| `add` | 지정한 path에 대해 `git add`를 실행합니다. |
| `commit` | `git commit`을 실행합니다. |
| `checkout` | `git checkout`을 실행하고, 옵션으로 branch를 생성합니다. |

## API 설명

대부분의 tool은 내부 `git` 프로세스 결과인 `{ "exitCode": number, "stdout": string, "stderr": string }` 형태를 반환합니다.

| Tool | Arguments | Git 명령 |
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
| `checkout` | `repositoryPath` string, `target` string, `createBranch` bool = `false` | `git checkout` 또는 `git checkout -b` |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | repository path로 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `MCP_ENABLE_GIT_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 `init/add/commit/checkout`을 막습니다. |
