# mcp-loki Air Gap 사용

인터넷이 되는 환경에서 이미지를 빌드하고 tar 파일로 추출합니다.

```powershell
docker build -t local/mcp-loki ..
docker save -o .\local-mcp-loki.tar local/mcp-loki:latest
```

air gap 환경으로 `local-mcp-loki.tar`를 복사한 뒤 로드합니다.

```powershell
docker load -i .\local-mcp-loki.tar
```

실행 예시:

```powershell
docker run --rm -p 8093:8080 `
  -e "LOKI_BASE_URL=http://loki-gateway.monitoring.svc.cluster.local" `
  local/mcp-loki
```

실제 Loki endpoint도 air gap 네트워크 내부에서 접근 가능해야 합니다.

