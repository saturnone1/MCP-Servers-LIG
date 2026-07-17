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
.\scripts\run-docker-mcp.ps1 -Server mcp-kubernetes -Port 8087
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
| `run_kubectl` | `args` string array, `timeoutMs` int = `600000` (up to 24 hours, 64 MiB output) |

## Environment

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | Container roots allowed for YAML manifest files. |
| `MCP_PATH_MAPPINGS` | empty | Maps Windows host paths to container paths. |
| `MCP_ENABLE_KUBERNETES_WRITES` | `true` | Set `false` to block apply/delete/restart/scale and raw kubectl. |
| `MCP_ENABLE_RAW_KUBECTL` | `true` | Set `false` to block only `run_kubectl`. |
| `KUBECTL_PATH` | `kubectl` | kubectl executable path. |

## Kubernetes

Kubernetes manifests are available in [k8s/](k8s/README.ko.md). The in-cluster deployment uses a ServiceAccount with an unrestricted ClusterRole/ClusterRoleBinding and no Pod CPU or memory limit. Cluster admission policies and node capacity still apply.
