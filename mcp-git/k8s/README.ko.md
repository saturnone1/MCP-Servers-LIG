# mcp-git Kubernetes 배포

이 매니페스트는 `mcp-git`을 Kubernetes의 `mcp-servers` namespace에 배포합니다.

## 적용

```powershell
kubectl apply -f .\mcp-git\k8s\
```

## 접속 확인

```powershell
kubectl -n mcp-servers port-forward svc/mcp-git 8082:8080
```

## 호환성

- `/workspace`는 PVC(`mcp-git-workspace`)로 마운트됩니다.
- 로컬 Git 조회/commit은 PVC 안 repository 기준으로 동작합니다.
- remote fetch/push/pull은 cluster egress 또는 내부 Git 서버 접근이 필요합니다.
- air gap cluster에서는 image를 node runtime에 load하거나 내부 registry 경로로 바꿔야 합니다.
