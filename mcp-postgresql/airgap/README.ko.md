# mcp-postgresql Air Gap 사용

인터넷이 되는 환경에서 이미지를 빌드하고 tar 파일로 추출합니다.

```powershell
docker build -t local/mcp-postgresql ..
docker save -o .\local-mcp-postgresql.tar local/mcp-postgresql:latest
```

air gap 환경으로 `local-mcp-postgresql.tar`를 복사한 뒤 로드합니다.

```powershell
docker load -i .\local-mcp-postgresql.tar
```

실행 예시:

```powershell
docker run --rm -p 127.0.0.1:8090:8080 `
  -e "POSTGRES_CONNECTION_STRING=Host=<postgres>;Database=postgres;Username=<user>;Password=<password>" `
  local/mcp-postgresql
```

MCP 연결 주소:

- HTTP: `http://localhost:8090/mcp`
- SSE: `http://localhost:8090/sse`

실제 PostgreSQL 서버도 air gap 네트워크 내부에서 접근 가능해야 합니다.

