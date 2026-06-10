# mcp-git Air Gap 사용법

이 폴더는 `local/mcp-git:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-git
```

생성 파일:

```text
mcp-git\airgap\local-mcp-git.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-git\airgap\local-mcp-git.tar
```

## 실행

```powershell
docker run -d --name mcp-git -p 8082:8080 `
  -v C:\:/host/c `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-git:latest
```

연결 주소:

- HTTP: `http://localhost:8082/mcp`
- SSE: `http://localhost:8082/sse`

## Air Gap 참고

이미지 안에 `git` CLI가 포함됩니다. 로컬 repository 조회/commit 등은 인터넷 없이 동작합니다. 원격 저장소에 대한 `fetch`, `pull`, `push` 같은 네트워크 작업은 air gap 네트워크 정책에 따릅니다.
