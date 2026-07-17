# mcp-prometheus

영어 버전: [README.md](README.md)

Prometheus HTTP API를 MCP tool로 제공하는 C# 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: Prometheus HTTP API를 호출하는 C# MCP 서버입니다.
- 런타임 요구사항: `PROMETHEUS_BASE_URL`이 접근 가능한 Prometheus 서버를 가리켜야 합니다.

## 빌드

```powershell
docker build -t local/mcp-prometheus .
```

## Air Gap 추출

`local/mcp-prometheus:latest` 이미지를 `airgap/local-mcp-prometheus.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
docker run --rm -p 127.0.0.1:8089:8080 `
  -e "PROMETHEUS_BASE_URL=http://host.docker.internal:9090" `
  local/mcp-prometheus
```

연결 주소:

- HTTP: `http://localhost:8089/mcp`
- SSE: `http://localhost:8089/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `config` | 설정된 Prometheus base URL을 반환합니다. |
| `ready` | `/-/ready`를 호출합니다. |
| `query` | instant query를 실행합니다. |
| `query_range` | range query를 실행합니다. |
| `labels` | label 목록을 조회합니다. |
| `label_values` | label value 목록을 조회합니다. |
| `targets` | scrape target 목록을 조회합니다. |
| `alerts` | alert 목록을 조회합니다. |
| `series` | selector와 일치하는 series를 조회합니다. |

## API 설명

tool은 `{ "statusCode": number, "success": bool, "body": string }` 형태를 반환합니다.

| Tool | Arguments |
| --- | --- |
| `config` | 없음 |
| `ready` | 없음 |
| `query` | `query` string, `time` string? = `null`, `timeoutSeconds` int = `30` |
| `query_range` | `query` string, `start` string, `end` string, `step` string, `timeoutSeconds` int = `60` |
| `labels` | `start` string? = `null`, `end` string? = `null`, `match` string array? = `null` |
| `label_values` | `label` string, `start` string? = `null`, `end` string? = `null`, `match` string array? = `null` |
| `targets` | `state` string = `any` |
| `alerts` | 없음 |
| `series` | `match` string array, `start` string? = `null`, `end` string? = `null` |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. Kubernetes 배포에서는 ConfigMap의 `PROMETHEUS_BASE_URL`을 사용하며 기본값은 `http://prometheus-server.monitoring.svc.cluster.local:9090`입니다. `mcp-servers` 네임스페이스에서 Prometheus service에 접근 가능하면 클러스터 네이티브로 동작합니다.
