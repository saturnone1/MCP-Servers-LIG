# mcp-loki Kubernetes 배포

이 매니페스트는 `mcp-loki`를 `mcp-servers` 네임스페이스에 배포합니다.

## 적용

`configmap.yaml`의 `LOKI_BASE_URL`을 실제 Loki gateway/service 주소로 바꿉니다. 인증이 필요한 경우 `secret.example.yaml`의 값을 채운 뒤 적용합니다.

```powershell
kubectl apply -f .\mcp-loki\k8s\namespace.yaml
kubectl apply -f .\mcp-loki\k8s\secret.example.yaml
kubectl apply -f .\mcp-loki\k8s\configmap.yaml
kubectl apply -f .\mcp-loki\k8s\deployment.yaml
kubectl apply -f .\mcp-loki\k8s\service.yaml
```

로컬에서 확인하려면 다음처럼 포트 포워딩합니다.

```powershell
kubectl -n mcp-servers port-forward svc/mcp-loki 8093:8080
```

## 호환성

- Loki gateway 또는 query-frontend/query endpoint가 `mcp-servers` 네임스페이스에서 접근 가능해야 합니다.
- multi-tenant Loki는 `LOKI_TENANT_ID`로 `X-Scope-OrgID` header를 설정할 수 있습니다.
- air gap 클러스터에서는 `local/mcp-loki:latest` 이미지를 노드 런타임에 직접 로드하거나 내부 레지스트리 경로로 바꿔야 합니다.

