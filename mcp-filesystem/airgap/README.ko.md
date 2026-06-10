# mcp-filesystem Air Gap 사용법

이 폴더는 `local/mcp-filesystem:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-filesystem
```

생성 파일:

```text
mcp-filesystem\airgap\local-mcp-filesystem.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-filesystem\airgap\local-mcp-filesystem.tar
```

## 실행

```powershell
docker run -d --name mcp-filesystem -p 8081:8080 `
  -v C:\:/host/c `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-filesystem:latest
```

연결 주소:

- HTTP: `http://localhost:8081/mcp`
- SSE: `http://localhost:8081/sse`

## Air Gap 참고

파일시스템 서버는 로컬/마운트 파일만 사용합니다. 런타임 인터넷은 필요 없습니다. `C:\` 전체를 보려면 위 예시처럼 `C:\:/host/c` 마운트와 `MCP_PATH_MAPPINGS`가 필요합니다.
