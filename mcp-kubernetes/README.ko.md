# mcp-kubernetes

영어 버전: [README.md](README.md)

Kubernetes 작업을 MCP tool로 제공하는 C# 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: `kubectl` CLI를 감싸는 C# MCP 서버입니다.
- 런타임 요구사항: kubeconfig를 마운트하거나 cluster 내부 config가 있어야 합니다.
- trusted-local Docker 기본값: Kubernetes 변경 작업과 raw `kubectl` 실행을 허용합니다.

## 빌드

```powershell
docker build -t local/mcp-kubernetes .
```

## Air Gap 추출

`local/mcp-kubernetes:latest` 이미지를 `airgap/local-mcp-kubernetes.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-kubernetes -Port 8087
```

연결 주소:

- HTTP: `http://localhost:8087/mcp`
- SSE: `http://localhost:8087/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `version` | `kubectl` client/server version을 반환합니다. |
| `cluster_info` | Kubernetes cluster 정보를 조회합니다. |
| `list_namespaces` | namespace를 나열합니다. |
| `list_pods` | namespace별 또는 전체 pod를 나열합니다. |
| `pod_logs` | pod 로그를 조회합니다. |
| `list_deployments` | deployment를 나열합니다. |
| `apply_yaml` | YAML manifest 파일을 적용합니다. |
| `delete_resource` | kind/name으로 resource를 삭제합니다. |
| `rollout_restart` | deployment rollout을 재시작합니다. |
| `scale_deployment` | deployment replica 수를 조정합니다. |
| `generate_deployment_yaml` | 간단한 Deployment YAML을 생성합니다. |
| `run_kubectl` | raw `kubectl` 인자를 실행합니다. |

## API 설명

대부분의 tool은 `{ "exitCode": number, "stdout": string, "stderr": string }` 형태를 반환합니다.

| Tool | Arguments |
| --- | --- |
| `version` | `clientOnly` bool = `true` |
| `cluster_info` | 없음 |
| `list_namespaces` | `output` string = `json` |
| `list_pods` | `ns` string? = `null`, `allNamespaces` bool = `false`, `output` string = `json` |
| `pod_logs` | `podName` string, `ns` string? = `null`, `container` string? = `null`, `tailLines` int = `200`, `previous` bool = `false` |
| `list_deployments` | `ns` string? = `null`, `allNamespaces` bool = `false`, `output` string = `json` |
| `apply_yaml` | `path` string, `ns` string? = `null` |
| `delete_resource` | `kind` string, `name` string, `ns` string? = `null` |
| `rollout_restart` | `deploymentName` string, `ns` string? = `null` |
| `scale_deployment` | `deploymentName` string, `replicas` int, `ns` string? = `null` |
| `generate_deployment_yaml` | `name` string, `image` string, `replicas` int = `1`, `containerPort` int = `80`, `ns` string? = `null` |
| `run_kubectl` | `args` string array, `timeoutMs` int = `600000` (최대 24시간, 출력 64 MiB) |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | YAML manifest 파일 접근을 허용할 컨테이너 root입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 컨테이너 경로로 매핑합니다. |
| `MCP_ENABLE_KUBERNETES_WRITES` | `true` | `false`로 설정하면 apply/delete/restart/scale 및 raw kubectl을 차단합니다. |
| `MCP_ENABLE_RAW_KUBECTL` | `true` | `false`로 설정하면 `run_kubectl`만 별도로 차단합니다. |
| `KUBECTL_PATH` | `kubectl` | kubectl 실행 파일 경로입니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. 클러스터 내부 배포는 kubeconfig 대신 모든 API group/resource/verb를 허용하는 ClusterRole/ClusterRoleBinding을 사용하며 Pod CPU·메모리 limit을 두지 않습니다. 클러스터 admission 정책과 노드 용량은 그대로 적용됩니다.
