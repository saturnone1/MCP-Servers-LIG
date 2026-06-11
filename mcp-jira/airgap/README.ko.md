# mcp-jira Air Gap 사용

인터넷이 되는 환경에서 이미지를 빌드하고 tar 파일로 추출합니다.

```powershell
docker build -t local/mcp-jira ..
docker save -o .\local-mcp-jira.tar local/mcp-jira:latest
```

air gap 환경으로 `local-mcp-jira.tar`를 복사한 뒤 로드합니다.

```powershell
docker load -i .\local-mcp-jira.tar
```

실행 예시:

```powershell
docker run --rm -p 8092:8080 `
  -e "JIRA_BASE_URL=https://jira.example.local" `
  -e "JIRA_BEARER_TOKEN=<token>" `
  local/mcp-jira
```

실제 Jira 인스턴스도 air gap 네트워크 내부에서 접근 가능해야 합니다.

