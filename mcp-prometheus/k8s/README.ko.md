# mcp-prometheus Kubernetes 배포

이 매니페스트는 `mcp-prometheus`를 `mcp-servers` 네임스페이스에 배포합니다.

## 적용

```powershell
kubectl apply -f .\mcp-prometheus\k8s\
```

서비스는 `ClusterIP`이며 클러스터 내부 주소는 `http://mcp-prometheus.mcp-servers.svc.cluster.local:8080/mcp` 입니다.

로컬에서 확인하려면 다음처럼 포트 포워딩합니다.

```powershell
kubectl -n mcp-servers port-forward svc/mcp-prometheus 8089:8080
```

## 설정

기본 Prometheus 주소는 `configmap.yaml`의 `PROMETHEUS_BASE_URL`에 있습니다.

```text
http://prometheus-server.monitoring.svc.cluster.local:9090
```

인증 토큰이 필요한 환경에서는 `deployment.yaml`의 `PROMETHEUS_BEARER_TOKEN` secret 참조 주석을 풀고 다음 형태의 Secret을 별도로 만듭니다.

```powershell
kubectl -n mcp-servers create secret generic mcp-prometheus-auth `
  --from-literal=bearer-token="<token>"
```

## 호환성

- Prometheus service가 `mcp-servers` 네임스페이스에서 네트워크로 접근 가능해야 합니다.
- air gap 클러스터에서는 `local/mcp-prometheus:latest` 이미지를 노드 런타임에 직접 로드하거나 내부 레지스트리 경로로 바꿔야 합니다.

