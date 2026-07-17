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
- 기본 매니페스트는 모든 API group, resource, verb를 허용하는 ClusterRole/ClusterRoleBinding을 사용합니다.
- 모든 namespace의 조회·생성·수정·삭제와 raw kubectl을 사용할 수 있습니다.
- Pod CPU/메모리 limit은 두지 않으며 실제 사용량은 클러스터 정책과 노드 용량을 따릅니다.
