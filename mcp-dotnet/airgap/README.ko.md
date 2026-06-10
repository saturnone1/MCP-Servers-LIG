# mcp-dotnet Air Gap 사용법

이 폴더는 `local/mcp-dotnet:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-dotnet
```

생성 파일:

```text
mcp-dotnet\airgap\local-mcp-dotnet.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-dotnet\airgap\local-mcp-dotnet.tar
```

## 실행

```powershell
docker run -d --name mcp-dotnet -p 8084:8080 `
  -v C:\:/host/c `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-dotnet:latest
```

연결 주소:

- HTTP: `http://localhost:8084/mcp`
- SSE: `http://localhost:8084/sse`

## Air Gap 참고

.NET SDK는 이미지 안에 포함됩니다. `sdk_info`, `list_projects`, 이미 restore된 프로젝트의 `build/test`는 인터넷 없이 동작합니다. `restore`와 `add_package`는 NuGet 패키지가 이미지 캐시나 내부 오프라인 feed에 없으면 실패할 수 있습니다.
