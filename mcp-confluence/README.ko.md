# mcp-confluence

Confluence Data Center/Server REST API를 호출하는 C# 원격 MCP 서버입니다. Streamable HTTP와 legacy SSE를 지원합니다.

## 호환성

Confluence Cloud REST API v2가 아니라 Data Center/Server의 `/rest/api/...` REST v1 경로를 사용합니다. Confluence Server 5.5+, Data Center 5.6+, 8.5 LTS, 9.2 LTS/9.2.9, 최신 10.x Data Center까지 이어지는 REST 표면을 우선으로 잡았습니다.

- `/rest/api/user/current`
- `/rest/api/settings/systemInfo`
- `/rest/troubleshooting/1.0/pre-upgrade/info`
- `/rest/api/space`
- `/rest/api/content/search`
- `/rest/api/content/{id}`
- `/rest/api/content/{id}/child/page`
- `/rest/api/content`

## 빌드

```powershell
docker build -t local/mcp-confluence .
```

## 실행

```powershell
docker run --rm -p 127.0.0.1:42198:8080 `
  -e "CONFLUENCE_BASE_URL=https://confluence.example.local" `
  -e "CONFLUENCE_BEARER_TOKEN=<token>" `
  local/mcp-confluence
```

MCP 클라이언트는 Streamable HTTP `http://localhost:42198/mcp` 또는 legacy SSE `http://localhost:42198/sse`로 연결합니다.

## 도구

| 도구 | 설명 |
| --- | --- |
| `config` | Confluence URL, 인증 설정 상태, 호환성 정보를 반환합니다. |
| `server_info` | 대상 서버가 제공하는 경우 버전/서버 정보를 조회합니다. |
| `current_user` | 현재 Confluence 사용자를 조회합니다. |
| `list_spaces` | Space 목록을 조회합니다. |
| `get_space` | key로 Space 하나를 조회합니다. |
| `list_content` | `/rest/api/content` query parameter 방식으로 콘텐츠를 조회합니다. |
| `search_content` | CQL로 콘텐츠를 검색합니다. |
| `get_content` | ID로 콘텐츠를 조회합니다. |
| `list_child_pages` | 특정 콘텐츠 아래의 하위 페이지를 조회합니다. |
| `create_page` | storage 형식 본문으로 페이지를 생성합니다. |
| `update_page` | 다음 version 번호로 페이지를 갱신합니다. |
| `delete_content` | 콘텐츠를 삭제 또는 휴지통 처리합니다. |

목록·검색 tool은 요청당 최대 100개를 기본 사용하며 `start`로 계속 조회할 수 있습니다.

## 환경변수

| 변수 | 기본값 | 용도 |
| --- | --- | --- |
| `CONFLUENCE_BASE_URL` | `http://confluence.local` | Confluence 기본 URL입니다. context path가 있으면 `https://host/confluence`처럼 포함합니다. |
| `CONFLUENCE_BEARER_TOKEN` | empty | Bearer token 인증입니다. |
| `CONFLUENCE_PAT` | empty | Bearer token 별칭입니다. Data Center personal access token을 넣기 좋습니다. |
| `CONFLUENCE_USERNAME` | empty | Basic 인증 사용자입니다. |
| `CONFLUENCE_API_TOKEN` | empty | Basic 인증 토큰 또는 비밀번호입니다. |
| `CONFLUENCE_PASSWORD` | empty | `CONFLUENCE_API_TOKEN`이 없을 때 쓰는 fallback secret입니다. |
| `CONFLUENCE_COOKIE` | empty | SSO/session proxy 환경용 원시 Cookie 헤더입니다. 예: `JSESSIONID=...` |
| `MCP_ENABLE_CONFLUENCE_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 create/update/delete 도구를 차단합니다. |

