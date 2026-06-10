# mcp-kubernetes

Korean version: [README.ko.md](README.ko.md)

C# remote MCP server for Kubernetes operations over Streamable HTTP and legacy SSE.

## Lineage

- Upstream / porting source: none.
- Strategy: direct C# MCP server wrapping `kubectl`.
- Runtime requirement: a kubeconfig must be mounted or Kubernetes in-cluster config must be available.
- Trusted-local Docker defaults enable mutating Kubernetes tools and raw `kubectl`.

## Build

```powershell
docker build -t local/mcp-kubernetes .
```

## Air Gap Export

Use [airgap/README.ko.md](airgap/README.ko.md) to export `local/mcp-kubernetes:latest` as `airgap/local-mcp-kubernetes.tar`, copy it to an air-gapped machine, load it with `docker load`, and run it.

## Run

```powershell
docker run --rm -p 8087:8080 `
  -v C:\:/host/c `
  -v $HOME\.kube:/root/.kube `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-kubernetes
```

Connect MCP clients with Streamable HTTP at `http://localhost:8087/mcp` or legacy SSE at `http://localhost:8087/sse`.

## Tools

| Tool | What it does |
| --- | --- |
| `version` | Returns `kubectl` client/server version. |
| `cluster_info` | Shows Kubernetes cluster info. |
| `list_namespaces` | Lists namespaces. |
| `list_pods` | Lists pods by namespace or all namespaces. |
| `pod_logs` | Reads pod logs. |
| `list_deployments` | Lists deployments. |
| `apply_yaml` | Applies a YAML manifest file. |
| `delete_resource` | Deletes a resource by kind/name. |
| `rollout_restart` | Restarts a deployment rollout. |
| `scale_deployment` | Scales a deployment. |
| `generate_deployment_yaml` | Generates simple Deployment YAML. |
| `run_kubectl` | Runs raw `kubectl` arguments. |

## API Reference

Most tools return `{ "exitCode": number, "stdout": string, "stderr": string }`.

| Tool | Arguments |
| --- | --- |
| `version` | `clientOnly` bool = `true` |
| `cluster_info` | none |
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

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). In-cluster deployment uses a ServiceAccount, namespace-scoped Role, and RoleBinding instead of mounting a kubeconfig. This server is cluster-native, but RBAC must explicitly grant the read/write operations you expect the MCP tools to perform.
