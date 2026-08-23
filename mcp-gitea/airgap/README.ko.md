# mcp-gitea Air Gap 사용

인터넷이 되는 환경에서 이미지를 빌드하고 tar 파일로 추출합니다.

```powershell
docker build -t local/mcp-gitea ..
docker save -o .\local-mcp-gitea.tar local/mcp-gitea:latest
```

air gap 환경으로 `local-mcp-gitea.tar`를 복사한 뒤 로드합니다.

```powershell
docker load -i .\local-mcp-gitea.tar
```

실행 예시:

```powershell
docker run --rm -p 127.0.0.1:8099:8080 `
  -e "GITEA_BASE_URL=https://gitea.example.local" `
  -e "GITEA_TOKEN=<token>" `
  local/mcp-gitea
```

Gitea 대상도 air gap 네트워크 내부에서 접근 가능해야 합니다.
