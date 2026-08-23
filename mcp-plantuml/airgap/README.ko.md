# mcp-plantuml Air Gap 사용

인터넷이 되는 환경에서 이미지를 빌드하고 tar 파일로 추출합니다.

```powershell
docker build -t local/mcp-plantuml ..
docker save -o .\local-mcp-plantuml.tar local/mcp-plantuml:latest
```

air gap 환경으로 `local-mcp-plantuml.tar`를 복사한 뒤 로드합니다.

```powershell
docker load -i .\local-mcp-plantuml.tar
```

실행 예시:

```powershell
docker run --rm -p 127.0.0.1:8100:8080 `
  -e "MCP_ALLOWED_DIRS=/workspace" `
  -e "PLANTUML_PATH=plantuml" `
  local/mcp-plantuml
```

PlantUML 대상도 air gap 네트워크 내부에서 접근 가능해야 합니다.
