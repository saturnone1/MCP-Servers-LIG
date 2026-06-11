# mcp-loki

영어 버전: [README.md](README.md)

Loki 로그 분석을 제공하는 C# MCP 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: Loki HTTP API를 직접 호출하는 신규 C# MCP 서버입니다.
- 런타임 요구사항: `LOKI_BASE_URL`이 접근 가능한 Loki 또는 Loki gateway endpoint를 가리켜야 합니다.

## 빌드

```powershell
docker build -t local/mcp-loki .
```

## Air Gap 추출

`local/mcp-loki:latest` 이미지를 `airgap/local-mcp-loki.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
docker run --rm -p 8093:8080 `
  -e "LOKI_BASE_URL=http://host.docker.internal:3100" `
  local/mcp-loki
```

연결 주소:

- HTTP: `http://localhost:8093/mcp`
- SSE: `http://localhost:8093/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `config` | Loki base URL과 인증 설정 상태를 반환합니다. |
| `ready` | `/ready`를 호출합니다. |
| `query` | instant LogQL query를 실행합니다. |
| `query_range` | LogQL range query를 실행합니다. |
| `recent_logs` | stream selector의 최근 로그를 조회합니다. |
| `search_logs` | `|=` 또는 `|~` 필터를 붙여 최근 로그를 검색합니다. |
| `labels` | Loki label 목록을 조회합니다. |
| `label_values` | label value 목록을 조회합니다. |
| `series` | selector와 일치하는 series를 조회합니다. |
| `index_stats` | stream selector의 index stats를 조회합니다. |

## API 설명

tool은 `{ "statusCode": number, "success": bool, "body": string }` 형태를 반환합니다.

| Tool | Arguments |
| --- | --- |
| `config` | 없음 |
| `ready` | 없음 |
| `query` | `query` string, `time` string? = `null`, `limit` int = `100`, `direction` string = `backward` |
| `query_range` | `query` string, `start` string, `end` string, `step` string? = `null`, `limit` int = `100`, `direction` string = `backward` |
| `recent_logs` | `selector` string, `sinceMinutes` int = `30`, `limit` int = `200`, `direction` string = `backward` |
| `search_logs` | `selector` string, `pattern` string, `sinceMinutes` int = `30`, `limit` int = `200`, `regex` bool = `false`, `direction` string = `backward` |
| `labels` | `start` string? = `null`, `end` string? = `null` |
| `label_values` | `label` string, `start` string? = `null`, `end` string? = `null` |
| `series` | `match` string array, `start` string? = `null`, `end` string? = `null` |
| `index_stats` | `query` string, `start` string? = `null`, `end` string? = `null` |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `LOKI_BASE_URL` | `http://host.docker.internal:3100` | Loki 또는 Loki gateway base URL입니다. |
| `LOKI_BEARER_TOKEN` | 빈 값 | 선택적 bearer token입니다. |
| `LOKI_USERNAME` | 빈 값 | 선택적 basic auth username입니다. |
| `LOKI_PASSWORD` | 빈 값 | 선택적 basic auth password입니다. |
| `LOKI_TENANT_ID` | 빈 값 | 선택적 Loki multi-tenant `X-Scope-OrgID` header입니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. Kubernetes 배포에서는 ConfigMap의 `LOKI_BASE_URL`을 사용하며 기본값은 `http://loki-gateway.monitoring.svc.cluster.local`입니다. `mcp-servers` 네임스페이스에서 Loki endpoint에 접근 가능하면 클러스터 네이티브로 동작합니다.

