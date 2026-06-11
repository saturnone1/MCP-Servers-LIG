# mcp-postgresql Kubernetes 배포

이 매니페스트는 `mcp-postgresql`을 `mcp-servers` 네임스페이스에 배포합니다.

## 적용

먼저 `secret.example.yaml`의 connection string을 실제 값으로 바꿔 Secret을 적용합니다.

```powershell
kubectl apply -f .\mcp-postgresql\k8s\namespace.yaml
kubectl apply -f .\mcp-postgresql\k8s\secret.example.yaml
kubectl apply -f .\mcp-postgresql\k8s\configmap.yaml
kubectl apply -f .\mcp-postgresql\k8s\deployment.yaml
kubectl apply -f .\mcp-postgresql\k8s\service.yaml
```

로컬에서 확인하려면 다음처럼 포트 포워딩합니다.

```powershell
kubectl -n mcp-servers port-forward svc/mcp-postgresql 8090:8080
```

## 호환성

- PostgreSQL endpoint가 `mcp-servers` 네임스페이스에서 네트워크로 접근 가능해야 합니다.
- air gap 클러스터에서는 `local/mcp-postgresql:latest` 이미지를 노드 런타임에 직접 로드하거나 내부 레지스트리 경로로 바꿔야 합니다.

