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

## Air Gap 추출

`local/mcp-mssql:latest` 이미지를 `airgap/local-mcp-mssql.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
docker run --rm -p 127.0.0.1:8085:8080 -e MSSQL_CONNECTION_STRING="Server=host.docker.internal;Database=master;User Id=sa;Password=yourStrong(!)Password;TrustServerCertificate=True" local/mcp-mssql
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

## API 설명

SQL tool은 기본적으로 `MSSQL_CONNECTION_STRING`을 사용합니다. 호출별로 `connectionString`을 넘기면 해당 값을 우선 사용합니다.

| Tool | Arguments | 반환 |
| --- | --- | --- |
| `list_databases` | `connectionString` string? = `null` | database metadata 배열 |
| `list_schemas` | `connectionString` string? = `null` | schema metadata 배열 |
| `list_tables` | `connectionString` string? = `null`, `schema` string? = `null` | table metadata 배열 |
| `describe_table` | `tableName` string, `schema` string = `dbo`, `connectionString` string? = `null` | column metadata 배열 |
| `execute_read_query` | `sql` string, `connectionString` string? = `null`, `maxRows` int = `2000` | 최대 100,000행, read query timeout은 1시간입니다. |
| `execute_non_query` | `sql` string, `connectionString` string? = `null`, `timeoutSeconds` int = `30` | affected row 수와 실행 metadata |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MSSQL_CONNECTION_STRING` | 빈 값 | 기본 SQL Server connection string입니다. |
| `MCP_ENABLE_SQL_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 `execute_non_query`를 막습니다. |

## 참고

`execute_read_query`는 의도적으로 `SELECT` 또는 `WITH`로 시작하는 query만 허용합니다. 변경 SQL은 trusted 환경에서 `execute_non_query`를 사용합니다.

## Kubernetes

이번 단계에서는 `mcp-mssql`용 Kubernetes 매니페스트를 제공하지 않습니다. 요청한 Kubernetes 대상에서 명시적으로 제외되었고, 실제 클러스터 배포에는 SQL connection string, Secret, NetworkPolicy, DB egress 정책을 별도로 정해야 합니다.
