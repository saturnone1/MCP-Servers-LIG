# mcp-gitea Kubernetes 배포

이 매니페스트는 `mcp-gitea`을 `mcp-servers` 네임스페이스에 배포합니다.

## 적용

`secret.example.yaml`의 인증 값과 `configmap.yaml`의 값을 실제 환경에 맞게 바꾼 뒤 적용합니다.

```powershell
kubectl apply -f .\mcp-gitea\k8s\namespace.yaml
kubectl apply -f .\mcp-gitea\k8s\secret.example.yaml
kubectl apply -f .\mcp-gitea\k8s\configmap.yaml
kubectl apply -f .\mcp-gitea\k8s\deployment.yaml
kubectl apply -f .\mcp-gitea\k8s\service.yaml
```

로컬에서 확인하려면 다음처럼 포트 포워딩합니다.

```powershell
kubectl -n mcp-servers port-forward svc/mcp-gitea 8099:8080
```

포워딩한 뒤 Streamable HTTP는 `http://localhost:8099/mcp`, 레거시 SSE는 `http://localhost:8099/sse`로 접속합니다.
