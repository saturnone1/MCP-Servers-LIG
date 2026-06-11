# mcp-gitlab Air Gap 사용

인터넷이 되는 환경에서 이미지를 빌드하고 tar 파일로 추출합니다.

```powershell
docker build -t local/mcp-gitlab ..
docker save -o .\local-mcp-gitlab.tar local/mcp-gitlab:latest
```

air gap 환경으로 `local-mcp-gitlab.tar`를 복사한 뒤 로드합니다.

```powershell
docker load -i .\local-mcp-gitlab.tar
```

실행 예시:

```powershell
docker run --rm -p 8091:8080 `
  -e "GITLAB_BASE_URL=https://gitlab.example.local" `
  -e "GITLAB_TOKEN=<token>" `
  local/mcp-gitlab
```

실제 GitLab 인스턴스도 air gap 네트워크 내부에서 접근 가능해야 합니다.

