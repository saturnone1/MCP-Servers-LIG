# mcp-mssql Air Gap 사용법

이 폴더는 `local/mcp-mssql:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-mssql
```

생성 파일:

```text
mcp-mssql\airgap\local-mcp-mssql.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-mssql\airgap\local-mcp-mssql.tar
```

## 실행

```powershell
docker run -d --name mcp-mssql -p 8085:8080 `
  -e "MSSQL_CONNECTION_STRING=Server=host.docker.internal;Database=master;User Id=sa;Password=yourPassword;TrustServerCertificate=True" `
  local/mcp-mssql:latest
```

연결 주소:

- HTTP: `http://localhost:8085/mcp`
- SSE: `http://localhost:8085/sse`

## Air Gap 참고

런타임 인터넷은 필요 없지만, air gap 네트워크 안에서 접근 가능한 SQL Server와 `MSSQL_CONNECTION_STRING`이 필요합니다.
