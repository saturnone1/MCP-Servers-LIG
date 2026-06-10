# mcp-prometheus Air Gap 사용법

이 폴더는 `local/mcp-prometheus:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-prometheus
```

생성 파일:

```text
mcp-prometheus\airgap\local-mcp-prometheus.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-prometheus\airgap\local-mcp-prometheus.tar
```

## 실행

```powershell
docker run -d --name mcp-prometheus -p 8089:8080 `
  -e "PROMETHEUS_BASE_URL=http://prometheus.internal:9090" `
  local/mcp-prometheus:latest
```

연결 주소:

- HTTP: `http://localhost:8089/mcp`
- SSE: `http://localhost:8089/sse`

## Air Gap 참고

런타임 인터넷은 필요 없지만, air gap 네트워크 안에서 접근 가능한 Prometheus 서버가 필요합니다. 인증이 필요한 경우 `PROMETHEUS_BEARER_TOKEN`을 설정합니다.
