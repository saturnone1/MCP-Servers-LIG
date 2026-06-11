# mcp-jira

영어 버전: [README.md](README.md)

Jira REST API 작업을 제공하는 C# MCP 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: Jira REST API를 직접 호출하는 신규 C# MCP 서버입니다.
- 런타임 요구사항: `JIRA_BASE_URL`이 접근 가능한 Jira 인스턴스를 가리켜야 합니다. 인증은 `JIRA_BEARER_TOKEN` 또는 `JIRA_EMAIL` + `JIRA_API_TOKEN` 조합을 사용합니다.

## 빌드

```powershell
docker build -t local/mcp-jira .
```

## Air Gap 추출

`local/mcp-jira:latest` 이미지를 `airgap/local-mcp-jira.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
docker run --rm -p 8092:8080 `
  -e "JIRA_BASE_URL=https://jira.example.local" `
  -e "JIRA_BEARER_TOKEN=<token>" `
  local/mcp-jira
```

연결 주소:

- HTTP: `http://localhost:8092/mcp`
- SSE: `http://localhost:8092/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `config` | Jira base URL과 인증 설정 상태를 반환합니다. |
| `search_issues` | JQL로 issue를 검색합니다. |
| `get_issue` | issue key로 issue 하나를 조회합니다. |
| `create_issue` | issue를 생성합니다. |
| `add_comment` | issue에 comment를 추가합니다. |
| `list_transitions` | 사용 가능한 issue transition 목록을 조회합니다. |
| `transition_issue` | issue transition을 실행합니다. |
| `list_projects` | Jira project 목록을 조회합니다. |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `JIRA_BASE_URL` | `http://jira.local` | Jira base URL입니다. |
| `JIRA_BEARER_TOKEN` | 빈 값 | Jira Data Center 또는 호환 배포용 bearer token입니다. |
| `JIRA_EMAIL` | 빈 값 | Jira Cloud basic auth용 email입니다. |
| `JIRA_API_TOKEN` | 빈 값 | Jira Cloud basic auth용 API token입니다. |
| `MCP_ENABLE_JIRA_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 issue/comment/transition 쓰기를 막습니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. `mcp-servers` 네임스페이스에서 Jira endpoint에 접근 가능하고 credential을 Secret으로 제공하면 클러스터 네이티브로 동작합니다.

