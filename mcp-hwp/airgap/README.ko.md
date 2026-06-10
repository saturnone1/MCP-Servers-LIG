# mcp-hwp Air Gap 사용법

이 폴더는 `local/mcp-hwp:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-hwp
```

생성 파일:

```text
mcp-hwp\airgap\local-mcp-hwp.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-hwp\airgap\local-mcp-hwp.tar
```

## 실행

```powershell
docker run -d --name mcp-hwp -p 8086:8080 `
  -v C:\:/host/c `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-hwp:latest
```

연결 주소:

- HTTP: `http://localhost:8086/mcp`
- SSE: `http://localhost:8086/sse`

## Air Gap 참고

`pyhwp`, `hwp5txt`, LibreOffice, 한글 폰트가 이미지 안에 포함됩니다. `.hwp`/`.hwpx` 텍스트 추출과 `txt` 변환은 인터넷 없이 동작합니다. `docx`, `pdf`, `odt` 변환은 LibreOffice가 처리할 수 있는 문서에 한해 동작합니다.
