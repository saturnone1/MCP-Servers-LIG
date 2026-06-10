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
docker run --rm -p 8087:8080 `
  -v C:\:/host/c `
  -v $HOME\.kube:/root/.kube `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-kubernetes
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
| `run_kubectl` | `args` string array, `timeoutMs` int = `120000` |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. 클러스터 내부 배포에서는 kubeconfig를 마운트하지 않고 ServiceAccount, namespace-scoped Role, RoleBinding을 사용합니다. 이 서버는 클러스터 네이티브로 동작하지만, MCP tool이 수행할 read/write 작업은 RBAC에 명시적으로 허용되어 있어야 합니다.
