# mcp-shell Air Gap 사용법

이 폴더는 `local/mcp-shell:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-shell
```

생성 파일:

```text
mcp-shell\airgap\local-mcp-shell.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-shell\airgap\local-mcp-shell.tar
```

## 실행

```powershell
docker run -d --name mcp-shell -p 8083:8080 `
  -v C:\:/host/c `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-shell:latest
```

연결 주소:

- HTTP: `http://localhost:8083/mcp`
- SSE: `http://localhost:8083/sse`

## Air Gap 참고

컨테이너에 포함된 명령만 실행할 수 있습니다. 외부 다운로드나 패키지 설치 명령은 air gap 환경에서 실패할 수 있습니다.
