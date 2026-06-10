# mcp-kubernetes Kubernetes 배포

이 매니페스트는 `mcp-kubernetes`를 Kubernetes 내부에 배포하고 in-cluster ServiceAccount로 `kubectl`을 실행합니다.

## 적용

```powershell
kubectl apply -f .\mcp-kubernetes\k8s\
```

## 접속 확인

```powershell
kubectl -n mcp-servers port-forward svc/mcp-kubernetes 8087:8080
```

## 호환성

- kubeconfig를 마운트하지 않고 ServiceAccount token을 사용합니다.
- 기본 Role은 `mcp-servers` namespace 안의 pods/logs/services/configmaps/deployments 중심 권한만 제공합니다.
- `list_namespaces`, cluster-wide 조회, 다른 namespace 관리가 필요하면 별도 ClusterRole/ClusterRoleBinding을 추가해야 합니다.
- `apply_yaml`, `delete_resource`, `rollout_restart`, `scale_deployment`, `run_kubectl`은 RBAC 범위 안에서만 성공합니다.
