# mcp-confluence Kubernetes 배포

이 매니페스트는 `mcp-confluence`를 `mcp-servers` 네임스페이스에 배포합니다.

## 적용

`configmap.yaml`의 `CONFLUENCE_BASE_URL`과 `secret.example.yaml`의 credential을 실제 값으로 바꾼 뒤 적용합니다.

```powershell
kubectl apply -f .\mcp-confluence\k8s\namespace.yaml
kubectl apply -f .\mcp-confluence\k8s\secret.example.yaml
kubectl apply -f .\mcp-confluence\k8s\configmap.yaml
kubectl apply -f .\mcp-confluence\k8s\deployment.yaml
kubectl apply -f .\mcp-confluence\k8s\service.yaml
```

로컬에서 확인하려면 다음처럼 포트 포워딩합니다.

```powershell
kubectl -n mcp-servers port-forward svc/mcp-confluence 42198:8080
```

## 호환성

- Confluence Server 5.5+, Data Center 5.6+, 8.5 LTS, 9.2 LTS/9.2.9, 최신 10.x Data Center까지 이어지는 `/rest/api/...` REST API 경로를 사용합니다.
- Confluence endpoint가 `mcp-servers` 네임스페이스에서 접근 가능해야 합니다.
- air gap 클러스터에서는 내부 Confluence URL 또는 접근 가능한 사설 URL을 사용해야 합니다.
- 이미지는 노드 런타임에 직접 로드하거나 내부 레지스트리 경로로 바꿔야 합니다.

