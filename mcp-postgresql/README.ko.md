# mcp-postgresql

영어 버전: [README.md](README.md)

PostgreSQL 작업을 제공하는 C# MCP 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: `Npgsql`을 사용하는 신규 C# MCP 서버입니다.
- 런타임 요구사항: `POSTGRES_CONNECTION_STRING`이 접근 가능한 PostgreSQL 서버를 가리켜야 합니다.
- trusted-local Docker 기본값: `MCP_ENABLE_POSTGRES_WRITES=false`로 끄지 않는 한 non-query SQL tool을 허용합니다.

## 빌드

```powershell
docker build -t local/mcp-postgresql .
```

## Air Gap 추출

`local/mcp-postgresql:latest` 이미지를 `airgap/local-mcp-postgresql.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
docker run --rm -p 127.0.0.1:8090:8080 `
  -e "POSTGRES_CONNECTION_STRING=Host=host.docker.internal;Database=postgres;Username=postgres;Password=postgres" `
  local/mcp-postgresql
```

연결 주소:

- HTTP: `http://localhost:8090/mcp`
- SSE: `http://localhost:8090/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `config` | PostgreSQL connection string 설정 여부를 반환합니다. |
| `list_databases` | configured login이 볼 수 있는 database 목록을 반환합니다. |
| `list_schemas` | 현재 database의 schema 목록을 반환합니다. |
| `list_tables` | table 목록을 반환하고 optional schema filter를 지원합니다. |
| `describe_table` | table column metadata를 반환합니다. |
| `execute_read_query` | read-only SQL을 실행하고 row를 반환합니다. |
| `execute_non_query` | DDL/DML 같은 non-query SQL을 실행합니다. |

## API 설명

| Tool | Arguments | 반환 |
| --- | --- | --- |
| `config` | 없음 | 연결 설정 상태 |
| `list_databases` | `connectionString` string? = `null` | database metadata 배열 |
| `list_schemas` | `connectionString` string? = `null` | schema metadata 배열 |
| `list_tables` | `connectionString` string? = `null`, `schema` string? = `null` | table metadata 배열 |
| `describe_table` | `tableName` string, `schema` string = `public`, `connectionString` string? = `null` | column metadata 배열 |
| `execute_read_query` | `sql` string, `connectionString` string? = `null`, `maxRows` int = `2000` | 최대 100,000행, read query timeout은 1시간입니다. |
| `execute_non_query` | `sql` string, `connectionString` string? = `null`, `timeoutSeconds` int = `30` | affected row 수와 실행 metadata |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `POSTGRES_CONNECTION_STRING` | 빈 값 | 기본 PostgreSQL connection string입니다. |
| `MCP_ENABLE_POSTGRES_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 `execute_non_query`를 막습니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. `mcp-servers` 네임스페이스에서 PostgreSQL endpoint에 접근 가능하고 connection string을 Secret으로 제공하면 클러스터 네이티브로 동작합니다.

