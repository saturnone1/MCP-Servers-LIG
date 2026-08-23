# mcp-harbor

English version: [README.md](README.md)

Harbor v2 레지스트리 API를 Streamable HTTP와 레거시 SSE로 제공하는 C# 원격 MCP 서버입니다.

## 계보

- Harbor에는 공식 MCP 서버가 없습니다. 커뮤니티 구현이 둘 있고 모두 스스로 experimental이라고 밝힙니다. [bupd/harbor-mcp-server](https://github.com/bupd/harbor-mcp-server)는 조회 중심 13개(health, statistics, project summary, members, quotas, configurations, volumes, search), [nomagicln/mcp-harbor](https://github.com/nomagicln/mcp-harbor)는 CRUD 11개(project, repository, tag, Helm chart)를 제공합니다.
- 방식: 두 표면을 하나의 C# 서버로 합치고, 둘 다 다루지 않는 공백을 메웠습니다. Harbor v2는 태그 중심 모델을 **artifact** 중심으로 바꿨으므로 artifact 목록·상세, 태그 부착, 취약점 리포트, 빌드 히스토리, 스캔 실행을 추가했고 label, audit log, registry, replication, scanner, webhook 정책도 포함했습니다.
- Helm chart: v2는 차트를 폐기된 ChartMuseum이 아니라 OCI artifact로 저장하므로 `list_artifacts`가 이를 포괄합니다. `nomagicln/mcp-harbor`의 레거시 `chartrepo` 엔드포인트는 의도적으로 구현하지 않았습니다.
- 실행 조건: `HARBOR_BASE_URL`이 접근 가능한 Harbor 인스턴스를 가리켜야 합니다. `HARBOR_USERNAME`/`HARBOR_PASSWORD`에는 사용자 계정, CLI secret, robot 계정 중 하나를 넣습니다.

## 빌드

```powershell
docker build -t local/mcp-harbor .
```

## Air Gap 내보내기

[airgap/README.ko.md](airgap/README.ko.md)를 참고해 `local/mcp-harbor:latest`를 `airgap/local-mcp-harbor.tar`로 내보내고, air gap 장비로 복사한 뒤 `docker load`로 적재해 실행합니다.

## 실행

```powershell
docker run --rm -p 127.0.0.1:8101:8080 `
  -e "HARBOR_BASE_URL=https://harbor.example.local" `
  -e "HARBOR_USERNAME=<user>" `
  -e "HARBOR_PASSWORD=<password-or-cli-secret>" `
  local/mcp-harbor
```

MCP 클라이언트는 Streamable HTTP `http://localhost:8101/mcp` 또는 레거시 SSE `http://localhost:8101/sse`로 연결합니다.

## 도구

| 분류 | 도구 |
| --- | --- |
| 인스턴스 | `config`, `get_health`, `get_system_info`, `get_statistics`, `get_volumes`, `search` |
| 프로젝트 | `list_projects`, `get_project`, `get_project_summary`, `list_project_members`, `create_project`, `delete_project` |
| 저장소 | `list_repositories`, `get_repository`, `delete_repository` |
| Artifact | `list_artifacts`, `get_artifact`, `delete_artifact`, `get_build_history` |
| 태그 | `list_artifact_tags`, `create_tag`, `delete_tag` |
| 보안 | `get_vulnerabilities`, `scan_artifact`, `list_scanners` |
| 거버넌스 | `list_quotas`, `list_labels`, `list_audit_logs`, `list_webhook_policies` |
| 복제 | `list_registries`, `list_replication_policies`, `list_replication_executions`, `start_replication` |
| 시스템 | `get_configurations`, `update_configurations` |

`get_artifact`와 `list_artifacts`는 reference로 태그 이름과 digest를 모두 받으며, `with_scan_overview`로 취약점 요약을 같은 호출에서 함께 가져올 수 있습니다.

`update_configurations`는 `MCP_ENABLE_HARBOR_WRITES`에 더해 `confirm=true`를 요구합니다. Harbor 설정의 인증 값을 잘못 바꾸면 인스턴스 전체가 잠길 수 있기 때문입니다. 먼저 `get_configurations`로 현재 값을 확인하세요.

`team/service` 같은 중첩 저장소 이름은 Harbor API 규약대로 하나의 percent-encoded 경로 세그먼트로 전송합니다.

목록 도구의 기본 페이지 크기는 50이며 100으로 제한됩니다. 모든 목록 도구는 `page`를 지원합니다.

## 환경변수

| 변수 | 기본값 | 용도 |
| --- | --- | --- |
| `HARBOR_BASE_URL` | `http://harbor.local` | Harbor 기본 URL. `/api/v2.0`은 서버가 붙입니다. |
| `HARBOR_USERNAME` | 빈 값 | Harbor 계정, CLI secret 소유자, robot 계정 이름. |
| `HARBOR_PASSWORD` | 빈 값 | 비밀번호, CLI secret, robot 토큰. HTTP basic 인증으로 전송합니다. |
| `MCP_ENABLE_HARBOR_WRITES` | Dockerfile 기본 `true` | `false`로 두면 생성·삭제·스캔·복제·설정 변경 도구가 모두 차단됩니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. `mcp-servers` 네임스페이스에서 Harbor 엔드포인트에 접근 가능하고 인증 정보를 Secret으로 제공하면 클러스터에서 그대로 동작합니다.
