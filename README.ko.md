# MCP 원격 서버 번들

영어 버전: [README.md](README.md)

이 저장소는 Docker로 각각 빌드할 수 있는 원격 MCP 서버 7개를 담고 있습니다. 모든 서버는 C#/.NET ASP.NET Core 앱이며 `ModelContextProtocol.AspNetCore`를 사용합니다. 공통으로 Streamable HTTP는 `/mcp`, legacy SSE는 `/sse`와 `/message`를 제공하고, 컨테이너 내부 포트 `8080`에서 동작하며 Docker health check용 `/healthz`를 제공합니다.

이 이미지들은 신뢰할 수 있는 로컬 테스트용입니다. 기본값은 쓰기/실행 기능을 열어 두고, 컨테이너 내부 허용 경로도 `/`로 둡니다. 다만 호스트 파일시스템 접근은 Docker 볼륨으로 마운트한 범위에만 한정됩니다.

## 서버 목록

| 서버 | 포트 | 원본 / 계보 | 구현 방식 | 주요 기능 |
| --- | ---: | --- | --- | --- |
| `mcp-office` | 8080 | `iOfficeAI/OfficeCLI` | OfficeCLI를 이미지에 포함하고 legacy `.doc`용 `antiword` 추가 | Office 문서 검사/읽기, 텍스트 추출, 문서 생성, batch 편집, 렌더/내보내기, raw OfficeCLI |
| `mcp-filesystem` | 8081 | `mark3labs/mcp-filesystem-server` 보안 모델 | `System.IO` 기반 C# 재구현 | 파일 읽기/쓰기/복사/이동/삭제, stat, 디렉터리 목록/검색, 허용 root 처리 |
| `mcp-git` | 8082 | `modelcontextprotocol/servers` Git 서버 동작 | `git` CLI를 감싸는 C# 래퍼 | status, log, diff, show, branch list, blame, grep, init/add/commit/checkout |
| `mcp-shell` | 8083 | 신규 로컬 구현 | C# `ProcessStartInfo` 명령 실행기 | 컨테이너 내부 명령 실행, timeout, 출력 제한, 선택적 command/env allowlist |
| `mcp-dotnet` | 8084 | `jongalloway/dotnet-mcp`에서 아이디어 차용 | `dotnet` CLI를 감싸는 C# 래퍼 | SDK 정보, 프로젝트 탐색, restore/build/test, add package, format |
| `mcp-mssql` | 8085 | `little-fort/mcp-dotnet-mssql` 동작 기반 | `Microsoft.Data.SqlClient` 기반 C# SQL Server 도구 | DB/schema/table 목록, table describe, read query, non-query SQL |
| `mcp-hwp` | 8086 | 오픈 도구 기반 신규 구현 | `pyhwp`/`hwp5txt`, LibreOffice, ZIP/XML 파싱 | `.hwp`/`.hwpx` 텍스트 추출, 파일 검사, `txt/docx/pdf/odt` 변환 |

## 연결 주소

각 이미지는 컨테이너 내부 `8080` 포트에서 실행됩니다. smoke test에서 사용하는 호스트 포트 배치는 다음과 같습니다.

| 서버 | Streamable HTTP | Legacy SSE |
| --- | --- | --- |
| `mcp-office` | `http://localhost:8080/mcp` | `http://localhost:8080/sse` |
| `mcp-filesystem` | `http://localhost:8081/mcp` | `http://localhost:8081/sse` |
| `mcp-git` | `http://localhost:8082/mcp` | `http://localhost:8082/sse` |
| `mcp-shell` | `http://localhost:8083/mcp` | `http://localhost:8083/sse` |
| `mcp-dotnet` | `http://localhost:8084/mcp` | `http://localhost:8084/sse` |
| `mcp-mssql` | `http://localhost:8085/mcp` | `http://localhost:8085/sse` |
| `mcp-hwp` | `http://localhost:8086/mcp` | `http://localhost:8086/sse` |

## 전체 빌드

```powershell
$servers = 'mcp-office','mcp-filesystem','mcp-git','mcp-shell','mcp-mssql','mcp-dotnet','mcp-hwp'
foreach ($server in $servers) {
  docker build -t "local/$server" $server
}
```

런타임 이미지는 인터넷 없이 실행되는 것을 목표로 합니다. NuGet restore, apt/pip 설치, upstream 다운로드는 빌드 시점에 수행됩니다.

## 전체 smoke test

```powershell
.\tests\mcp-smoke.ps1
```

이 스크립트는 이미지를 빌드하고 컨테이너를 재시작한 뒤 `/healthz`, SSE, MCP tool 목록, 대표 tool 호출을 확인합니다. `MSSQL_CONNECTION_STRING`이 없으면 MSSQL 서버 기동과 tool discovery만 확인하고 실제 SQL 호출은 건너뜁니다.

smoke 호출 없이 7개 서버만 모두 실행하려면 다음 스크립트를 사용합니다.

```powershell
.\scripts\run-all.ps1
```

기본값으로 저장소는 `/workspace`, Windows `C:\` 드라이브는 `/host/c`에 마운트하고 `MCP_PATH_MAPPINGS=C:\=/host/c`를 설정합니다. 그래서 MCP 클라이언트가 `C:\Users\taewon\Desktop\넥스원\2024 분산스위치 논문.hwp` 같은 일반 Windows 경로를 그대로 넘겨도 컨테이너 내부 경로로 변환됩니다.

## Windows 경로 매핑

Linux 컨테이너 안에서 MCP 클라이언트가 넘긴 Windows 호스트 경로를 쓰려면 해당 호스트 폴더를 Docker로 마운트하고 `MCP_PATH_MAPPINGS`에 등록해야 합니다.

```powershell
docker run --rm -p 8081:8080 `
  -v C:\:/host/c `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-filesystem
```

같은 방식의 경로 매핑은 Office, filesystem, Git, shell, .NET, HWP처럼 파일 경로를 받는 서버에서 지원합니다. `MCP_ALLOWED_DIRS=/`는 컨테이너 내부 파일시스템을 여는 설정이지, Docker에 마운트하지 않은 호스트 폴더까지 자동으로 보이게 하지는 않습니다.

## 서버별 문서

각 서버 폴더에는 영어 `README.md`와 한국어 `README.ko.md`가 있습니다.

- `mcp-office/README.md`, `mcp-office/README.ko.md`
- `mcp-filesystem/README.md`, `mcp-filesystem/README.ko.md`
- `mcp-git/README.md`, `mcp-git/README.ko.md`
- `mcp-shell/README.md`, `mcp-shell/README.ko.md`
- `mcp-dotnet/README.md`, `mcp-dotnet/README.ko.md`
- `mcp-mssql/README.md`, `mcp-mssql/README.ko.md`
- `mcp-hwp/README.md`, `mcp-hwp/README.ko.md`
