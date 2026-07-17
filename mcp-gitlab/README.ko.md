# mcp-gitlab

영어 버전: [README.md](README.md)

GitLab REST API 작업을 제공하는 C# MCP 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: GitLab REST API를 직접 호출하는 신규 C# MCP 서버입니다.
- 런타임 요구사항: `GITLAB_BASE_URL`이 접근 가능한 GitLab 인스턴스를 가리켜야 합니다. private resource와 write 작업에는 `GITLAB_TOKEN`이 필요합니다.

## 빌드

```powershell
docker build -t local/mcp-gitlab .
```

## Air Gap 추출

`local/mcp-gitlab:latest` 이미지를 `airgap/local-mcp-gitlab.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
docker run --rm -p 127.0.0.1:8091:8080 `
  -e "GITLAB_BASE_URL=https://gitlab.example.local" `
  -e "GITLAB_TOKEN=<token>" `
  local/mcp-gitlab
```

연결 주소:

- HTTP: `http://localhost:8091/mcp`
- SSE: `http://localhost:8091/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `config` | GitLab base URL과 token 설정 여부를 반환합니다. |
| `list_projects` | 접근 가능한 project 목록을 반환합니다. |
| `get_project` | id 또는 path로 project 하나를 조회합니다. |
| `list_issues` | project issue 목록을 조회합니다. |
| `create_issue` | project issue를 생성합니다. |
| `list_merge_requests` | project merge request 목록을 조회합니다. |
| `get_file` | repository file을 읽습니다. |
| `create_or_update_file` | repository file을 생성하거나 수정합니다. |

목록 tool은 GitLab의 최대 page 크기인 100을 기본 사용하며 `page`로 계속 조회할 수 있습니다.

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `GITLAB_BASE_URL` | `http://gitlab.local` | GitLab base URL입니다. |
| `GITLAB_TOKEN` | 빈 값 | Personal/project/group access token입니다. |
| `MCP_ENABLE_GITLAB_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 issue/file 쓰기를 막습니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. `mcp-servers` 네임스페이스에서 GitLab endpoint에 접근 가능하고 token을 Secret으로 제공하면 클러스터 네이티브로 동작합니다.

