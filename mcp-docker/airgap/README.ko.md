# mcp-docker Air Gap 사용법

이 폴더는 `local/mcp-docker:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-docker
```

생성 파일:

```text
mcp-docker\airgap\local-mcp-docker.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-docker\airgap\local-mcp-docker.tar
```

## 실행

```powershell
docker run -d --name mcp-docker -p 8088:8080 `
  -v /var/run/docker.sock:/var/run/docker.sock `
  local/mcp-docker:latest
```

연결 주소:

- HTTP: `http://localhost:8088/mcp`
- SSE: `http://localhost:8088/sse`

## Air Gap 참고

Docker CLI는 이미지 안에 포함됩니다. Air gap 런타임에서는 Docker socket이 마운트되어야 합니다. `pull_image`는 내부 registry 또는 이미 접근 가능한 registry가 없으면 실패합니다.
