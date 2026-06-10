# mcp-filesystem Kubernetes 배포

이 매니페스트는 `mcp-filesystem`을 Kubernetes의 `mcp-servers` namespace에 배포합니다.

## 적용

```powershell
kubectl apply -f .\mcp-filesystem\k8s\
```

## 접속 확인

```powershell
kubectl -n mcp-servers port-forward svc/mcp-filesystem 8081:8080
```

- HTTP: `http://localhost:8081/mcp`
- SSE: `http://localhost:8081/sse`

## 호환성

- `/workspace`는 PVC(`mcp-filesystem-workspace`)로 마운트됩니다.
- `MCP_ALLOWED_DIRS=/workspace`로 제한합니다.
- 로컬 Docker 실행처럼 호스트 전체 경로를 자동으로 볼 수는 없습니다.
- air gap cluster에서는 `local/mcp-filesystem:latest`를 node runtime에 load하거나 내부 registry 경로로 바꿔야 합니다.
