# mcp-filesystem

영어 버전: [README.md](README.md)

파일시스템 접근을 제공하는 C# MCP 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 참고 원본: `mark3labs/mcp-filesystem-server`
- 구현 방식: Go 코드를 직접 포팅하기보다 `System.IO`로 C# 재구현했습니다.
- 유지한 보안 모델: allowed root, 경로 정규화, symlink-aware canonical path, 쓰기 gate.
- trusted-local Docker 기본값: 로컬 테스트가 쉽도록 쓰기를 허용하고 `MCP_ALLOWED_DIRS=/`로 둡니다.

## 빌드

```powershell
docker build -t local/mcp-filesystem .
```

## 실행

```powershell
docker run --rm -p 8081:8080 -v ${PWD}:/workspace local/mcp-filesystem
```

연결 주소:

- Streamable HTTP: `http://localhost:8081/mcp`
- Legacy SSE: `http://localhost:8081/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `list_allowed_directories` | 접근 허용된 컨테이너 root 경로를 나열합니다. |
| `read_file` | UTF-8 텍스트 파일을 읽습니다. |
| `read_multiple_files` | 여러 UTF-8 텍스트 파일을 한 번에 읽습니다. |
| `write_file` | UTF-8 텍스트 파일을 생성하거나 덮어씁니다. |
| `copy` | 파일 또는 디렉터리를 복사합니다. |
| `move` | 파일 또는 디렉터리를 이동합니다. |
| `delete` | 파일 또는 디렉터리를 삭제합니다. |
| `stat` | 파일 또는 디렉터리 메타데이터를 반환합니다. |
| `list_directory` | 패턴, 재귀, limit 옵션으로 디렉터리를 나열합니다. |
| `search` | 정규식으로 파일 이름을 검색합니다. |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `MCP_ENABLE_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 write/copy/move/delete를 막습니다. |
