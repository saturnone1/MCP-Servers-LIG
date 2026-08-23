# mcp-gitea

English version: [README.md](README.md)

Gitea API v1 작업을 Streamable HTTP와 레거시 SSE로 제공하는 C# 원격 MCP 서버입니다.

## 계보

- 참고한 도구 표면: 공식 [gitea/gitea-mcp](https://gitea.com/gitea/gitea-mcp) 서버(Go, MIT). 툴 카탈로그를 사양으로 삼았습니다.
- 방식: Gitea API v1을 직접 호출하는 C# MCP 서버입니다. 저장소의 다른 서버와 동일한 형태(`/healthz`, `/mcp`, `/sse`, 공유 .NET 런타임, 동일한 Dockerfile 베이스 이미지)를 유지합니다.
- 실행 조건: `GITEA_BASE_URL`이 접근 가능한 Gitea 인스턴스를 가리켜야 합니다. 비공개 리소스 조회와 모든 쓰기 도구에는 `GITEA_TOKEN`이 필요합니다.

## 빌드

```powershell
docker build -t local/mcp-gitea .
```

## Air Gap 내보내기

[airgap/README.ko.md](airgap/README.ko.md)를 참고해 `local/mcp-gitea:latest`를 `airgap/local-mcp-gitea.tar`로 내보내고, air gap 장비로 복사한 뒤 `docker load`로 적재해 실행합니다.

## 실행

```powershell
docker run --rm -p 127.0.0.1:8099:8080 `
  -e "GITEA_BASE_URL=https://gitea.example.local" `
  -e "GITEA_TOKEN=<token>" `
  local/mcp-gitea
```

MCP 클라이언트는 Streamable HTTP `http://localhost:8099/mcp` 또는 레거시 SSE `http://localhost:8099/sse`로 연결합니다.

## 도구

| 분류 | 도구 |
| --- | --- |
| 서버 | `config`, `get_version`, `get_me`, `list_my_orgs`, `list_notifications` |
| 검색 | `search_users`, `search_repos`, `search_issues` |
| 저장소 | `list_my_repos`, `list_org_repos`, `get_repo`, `create_repo`, `fork_repo` |
| 브랜치·태그 | `list_branches`, `create_branch`, `delete_branch`, `list_tags` |
| 커밋 | `list_commits`, `get_commit`, `get_repository_tree` |
| 파일 | `get_dir_contents`, `get_file_contents`, `create_or_update_file`, `delete_file` |
| 부분 편집 | `append_to_file`, `prepend_to_file`, `replace_in_file` |
| 이슈 | `list_issues`, `get_issue`, `create_issue`, `edit_issue`, `list_issue_comments`, `create_issue_comment` |
| Pull Request | `list_pull_requests`, `get_pull_request`, `get_pull_request_diff`, `create_pull_request`, `merge_pull_request` |
| 릴리스 | `list_releases`, `get_latest_release`, `create_release` |
| 메타데이터 | `list_labels`, `list_milestones`, `wiki_read`, `list_action_runs` |

`get_file_contents`는 Gitea가 돌려주는 base64 payload를 기본으로 디코딩해 텍스트로 반환합니다. blob sha가 포함된 원본 메타데이터가 필요하면 `decode=false`로 호출합니다.

부분 편집 도구는 현재 blob을 읽어 한 군데만 바꾸고 기존 sha와 함께 커밋하므로, 큰 파일을 모델이 통째로 다시 생성하지 않고도 수정할 수 있습니다.

목록 도구의 기본 페이지 크기는 50이며 Gitea 최대값인 100으로 제한됩니다. 모든 목록 도구는 `page`로 페이지를 넘길 수 있습니다.

## 환경변수

| 변수 | 기본값 | 용도 |
| --- | --- | --- |
| `GITEA_BASE_URL` | `http://gitea.local` | Gitea 기본 URL. |
| `GITEA_TOKEN` | 빈 값 | 개인 액세스 토큰. `Authorization: token <값>` 헤더로 전송합니다. |
| `MCP_ENABLE_GITEA_WRITES` | Dockerfile 기본 `true` | `false`로 두면 생성·수정·머지·삭제 도구가 모두 차단됩니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. `mcp-servers` 네임스페이스에서 Gitea 엔드포인트에 접근 가능하고 토큰을 Secret으로 제공하면 클러스터에서 그대로 동작합니다.
