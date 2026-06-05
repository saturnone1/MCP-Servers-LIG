# mcp-mssql

영어 버전: [README.md](README.md)

Microsoft SQL Server 작업을 MCP tool로 제공하는 C# 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 참고 원본: `little-fort/mcp-dotnet-mssql`
- 구현 방식: `Microsoft.Data.SqlClient`를 사용해 C#으로 구현했습니다.
- 런타임 요구사항: `MSSQL_CONNECTION_STRING` 환경 변수 또는 tool 호출별 connection string이 필요합니다.
- trusted-local Docker 기본값: non-query SQL 실행을 허용하지만, DB 접속 문자열은 여전히 명시해야 합니다.

## 빌드

```powershell
docker build -t local/mcp-mssql .
```

## 실행

```powershell
docker run --rm -p 8085:8080 -e MSSQL_CONNECTION_STRING="Server=host.docker.internal;Database=master;User Id=sa;Password=yourStrong(!)Password;TrustServerCertificate=True" local/mcp-mssql
```

연결 주소:

- Streamable HTTP: `http://localhost:8085/mcp`
- Legacy SSE: `http://localhost:8085/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `list_databases` | configured login이 볼 수 있는 database 목록을 반환합니다. |
| `list_schemas` | 현재 database의 schema 목록을 반환합니다. |
| `list_tables` | table 목록을 반환하고, optional schema filter를 지원합니다. |
| `describe_table` | table column metadata를 반환합니다. |
| `execute_read_query` | read-only `SELECT` 또는 `WITH` SQL을 실행하고 row를 반환합니다. |
| `execute_non_query` | DDL/DML 같은 non-query SQL을 실행합니다. |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MSSQL_CONNECTION_STRING` | 빈 값 | 기본 SQL Server connection string입니다. |
| `MCP_ENABLE_SQL_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 `execute_non_query`를 막습니다. |

## 참고

`execute_read_query`는 의도적으로 `SELECT` 또는 `WITH`로 시작하는 query만 허용합니다. 변경 SQL은 trusted 환경에서 `execute_non_query`를 사용합니다.
