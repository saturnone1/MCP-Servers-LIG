# mcp-harbor Air Gap 사용

인터넷이 되는 환경에서 이미지를 빌드하고 tar 파일로 추출합니다.

```powershell
docker build -t local/mcp-harbor ..
docker save -o .\local-mcp-harbor.tar local/mcp-harbor:latest
```

air gap 환경으로 `local-mcp-harbor.tar`를 복사한 뒤 로드합니다.

```powershell
docker load -i .\local-mcp-harbor.tar
```

실행 예시:

```powershell
docker run --rm -p 127.0.0.1:8101:8080 `
  -e "HARBOR_BASE_URL=https://harbor.example.local" `
  -e "HARBOR_USERNAME=<user>" `
  -e "HARBOR_PASSWORD=<password-or-cli-secret>" `
  local/mcp-harbor
```

Harbor 대상도 air gap 네트워크 내부에서 접근 가능해야 합니다.
