# MCP 원격 서버 번들

영어 버전: [README.md](README.md)

이 저장소는 Docker로 각각 빌드할 수 있는 원격 MCP 서버 14개와 Windows 호스트용 Rhapsody MCP 서버 1개를 담고 있습니다. 모든 서버는 C#/.NET ASP.NET Core 앱이며 `ModelContextProtocol.AspNetCore`를 사용합니다. 공통으로 Streamable HTTP는 `/mcp`, legacy SSE는 `/sse`와 `/message`를 제공하고 `/healthz`를 제공합니다.

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
| `mcp-kubernetes` | 8087 | 신규 로컬 구현 | `kubectl` CLI를 감싸는 C# 래퍼 | cluster 정보, namespace, pod, log, deployment, YAML 적용/삭제/재시작/scale/생성 |
| `mcp-docker` | 8088 | 신규 로컬 구현 | Docker CLI와 Docker socket을 사용하는 C# 래퍼 | container, image, inspect, logs, run/start/stop/remove, pull/remove image |
| `mcp-prometheus` | 8089 | 신규 로컬 구현 | Prometheus HTTP API C# client | readiness, instant/range query, label, target, alert, series |
| `mcp-postgresql` | 8090 | 신규 로컬 구현 | `Npgsql` 기반 C# PostgreSQL 도구 | DB/schema/table 목록, table describe, read query, non-query SQL |
| `mcp-gitlab` | 8091 | 신규 로컬 구현 | GitLab REST API C# client | project, issue, merge request, repository file |
| `mcp-jira` | 8092 | 신규 로컬 구현 | Jira REST API C# client | JQL 검색, issue, comment, transition, project |
| `mcp-loki` | 8093 | 신규 로컬 구현 | Loki HTTP API C# client | LogQL query, 최근 로그 검색, label, series, index stats |
| `mcp-rhapsody` | 8094 | 신규 로컬 구현 | Rhapsody COM/CLI/file 자동화를 위한 Windows 호스트 C# 서버 | Rhapsody 탐지, 모델 파일 inspect, 설정된 CLI 실행 |

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
| `mcp-kubernetes` | `http://localhost:8087/mcp` | `http://localhost:8087/sse` |
| `mcp-docker` | `http://localhost:8088/mcp` | `http://localhost:8088/sse` |
| `mcp-prometheus` | `http://localhost:8089/mcp` | `http://localhost:8089/sse` |
| `mcp-postgresql` | `http://localhost:8090/mcp` | `http://localhost:8090/sse` |
| `mcp-gitlab` | `http://localhost:8091/mcp` | `http://localhost:8091/sse` |
| `mcp-jira` | `http://localhost:8092/mcp` | `http://localhost:8092/sse` |
| `mcp-loki` | `http://localhost:8093/mcp` | `http://localhost:8093/sse` |
| `mcp-rhapsody` | `http://localhost:8094/mcp` | `http://localhost:8094/sse` |

## MCP API 형태

모든 서버는 같은 MCP transport API를 제공합니다. Tool 목록은 `/mcp`에서 `tools/list`로 조회하고, tool 실행은 `tools/call`로 호출합니다. Legacy 클라이언트는 `/sse`로 연결하고 `/message`로 메시지를 보낼 수 있습니다.

Streamable HTTP 호출 예시:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "extract_text",
    "arguments": {
      "path": "C:\\Users\\taewon\\Desktop\\넥스원\\2024 분산스위치 논문.hwp",
      "maxChars": 4000
    }
  }
}
```

정확한 tool 이름, 파라미터, 기본값, 반환 형태는 각 서버별 README에 정리되어 있습니다.

## 전체 빌드

```powershell
$servers = 'mcp-office','mcp-filesystem','mcp-git','mcp-shell','mcp-mssql','mcp-dotnet','mcp-hwp','mcp-kubernetes','mcp-docker','mcp-prometheus','mcp-postgresql','mcp-gitlab','mcp-jira','mcp-loki'
foreach ($server in $servers) {
  docker build -t "local/$server" $server
}
```

런타임 이미지는 인터넷 없이 실행되는 것을 목표로 합니다. NuGet restore, apt/pip 설치, upstream 다운로드는 빌드 시점에 수행됩니다.

`mcp-rhapsody`는 Docker 빌드 목록에 포함하지 않습니다. Windows 호스트 실행 패키지로 publish합니다.

```powershell
.\mcp-rhapsody\scripts\publish-win.ps1
```

## Air Gap 이미지 추출

인터넷이 되는 PC에서 이미지를 빌드하거나 준비한 뒤 Docker tar archive로 추출합니다.

```powershell
.\scripts\export-airgap.ps1
```

추출 전에 다시 빌드하려면 다음처럼 실행합니다.

```powershell
.\scripts\export-airgap.ps1 -Build
```

스크립트는 서버별 tar 파일을 각 폴더 아래에 생성합니다.

```text
mcp-office\airgap\local-mcp-office.tar
mcp-filesystem\airgap\local-mcp-filesystem.tar
mcp-git\airgap\local-mcp-git.tar
mcp-shell\airgap\local-mcp-shell.tar
mcp-dotnet\airgap\local-mcp-dotnet.tar
mcp-mssql\airgap\local-mcp-mssql.tar
mcp-hwp\airgap\local-mcp-hwp.tar
mcp-kubernetes\airgap\local-mcp-kubernetes.tar
mcp-docker\airgap\local-mcp-docker.tar
mcp-prometheus\airgap\local-mcp-prometheus.tar
mcp-postgresql\airgap\local-mcp-postgresql.tar
mcp-gitlab\airgap\local-mcp-gitlab.tar
mcp-jira\airgap\local-mcp-jira.tar
mcp-loki\airgap\local-mcp-loki.tar
```

필요한 `airgap` 폴더 또는 tar 파일을 air gap PC로 옮긴 뒤 `docker load -i <tar-file>`로 로드하면 됩니다. 각 서버 폴더의 `airgap/README.ko.md`에는 해당 서버의 정확한 load/run 명령이 들어 있습니다. tar archive는 Git에 커밋되지 않도록 제외했습니다.

## 검증

```powershell
.\tests\verify-priority.ps1 -SkipBuild -SkipImagePull
```

우선순위 검증 스크립트는 Docker MCP smoke, 외부 API mock 호출, PostgreSQL fixture, SQL Server fixture, Windows-host Rhapsody MCP smoke를 순서대로 실행합니다. Rhapsody가 설치된 Windows PC에서는 `-RhapsodyProjectPath "C:\path\model.rpyx"`를 추가하면 COM read smoke까지 실행하고, 모델 수정/저장이 안전할 때만 `-RunRhapsodyWriteSmoke`를 추가합니다.

Docker 서버만 빠르게 확인하려면 다음을 사용합니다.

```powershell
.\tests\mcp-smoke.ps1 -SkipBuild
```

Docker smoke test는 컨테이너를 재시작한 뒤 `/healthz`, SSE, MCP tool 목록, 대표 tool 호출을 확인합니다. Prometheus, GitLab, Jira, Loki는 로컬 mock HTTP API를 상대로 실제 HTTP 호출까지 확인합니다. PostgreSQL과 SQL Server의 실제 DB 호출은 `tests/`의 fixture 스크립트에서 확인합니다.

smoke 호출 없이 14개 서버를 모두 실행하려면 다음 스크립트를 사용합니다.

```powershell
.\scripts\run-all.ps1
```

기본값으로 저장소는 `/workspace`, Windows `C:\` 드라이브는 `/host/c`에 마운트하고 `MCP_PATH_MAPPINGS=C:\=/host/c`를 설정합니다. 그래서 MCP 클라이언트가 `C:\Users\taewon\Desktop\넥스원\2024 분산스위치 논문.hwp` 같은 일반 Windows 경로를 그대로 넘겨도 컨테이너 내부 경로로 변환됩니다.

API 기반 서버를 실제 내부 서비스에 붙여서 실행하려면 시작할 때 endpoint와 credential을 넘깁니다.

```powershell
.\scripts\run-all.ps1 `
  -PostgresConnectionString "Host=postgres.internal;Port=5432;Database=app;Username=mcp;Password=secret" `
  -MssqlConnectionString "Server=mssql.internal;Database=app;User Id=mcp;Password=secret;TrustServerCertificate=True" `
  -PrometheusBaseUrl "http://prometheus.monitoring.svc:9090" `
  -GitLabBaseUrl "https://gitlab.internal" `
  -GitLabToken "glpat-..." `
  -JiraBaseUrl "https://jira.internal" `
  -JiraBearerToken "..." `
  -LokiBaseUrl "http://loki.monitoring.svc:3100"
```

air gap 환경에서는 이 값들을 내부 서비스, 내부 DNS, 또는 로컬 mock/fixture 서비스 주소로 지정하면 됩니다. connection string이나 API URL을 생략해도 컨테이너는 뜨지만, 해당 backend가 필요한 tool은 설정 오류를 반환할 수 있습니다.

## Windows 경로 매핑

Linux 컨테이너 안에서 MCP 클라이언트가 넘긴 Windows 호스트 경로를 쓰려면 해당 호스트 폴더를 Docker로 마운트하고 `MCP_PATH_MAPPINGS`에 등록해야 합니다.

```powershell
docker run --rm -p 8081:8080 `
  -v C:\:/host/c `
  -e "MCP_PATH_MAPPINGS=C:\=/host/c" `
  local/mcp-filesystem
```

같은 방식의 경로 매핑은 Office, filesystem, Git, shell, .NET, HWP처럼 파일 경로를 받는 서버에서 지원합니다. `MCP_ALLOWED_DIRS=/`는 컨테이너 내부 파일시스템을 여는 설정이지, Docker에 마운트하지 않은 호스트 폴더까지 자동으로 보이게 하지는 않습니다.

## Kubernetes 배포

Linux Kubernetes workload로 자연스럽게 실행할 수 있는 MCP 서버에만 Kubernetes 매니페스트를 제공합니다.

- 포함: `mcp-filesystem`, `mcp-git`, `mcp-dotnet`, `mcp-kubernetes`, `mcp-prometheus`, `mcp-postgresql`, `mcp-gitlab`, `mcp-jira`, `mcp-loki`
- 이번 단계 제외: `mcp-office`, `mcp-shell`, `mcp-hwp`, `mcp-mssql`, `mcp-docker`

포함된 서버는 각 폴더 아래 `k8s/`에 namespace, Deployment, Service, 필요한 ConfigMap/PVC/RBAC 파일을 갖고 있습니다. 서버별 적용 예시는 다음과 같습니다.

```powershell
kubectl apply -f .\<server>\k8s\
```

기본 네임스페이스는 `mcp-servers`, 컨테이너 포트는 `8080`, Service는 `ClusterIP`, readiness/liveness probe는 `GET /healthz`입니다. 파일/프로젝트/repo 기반 서버는 로컬 Docker용 broad host-path 대신 `/workspace` PVC를 사용합니다.
PostgreSQL, GitLab, Jira, Loki처럼 외부 시스템에 연결하는 서버는 예시 Secret 매니페스트를 포함합니다. 실제 read/write API tool을 쓰기 전에 값을 수정하거나 동등한 Secret을 별도로 만들어야 합니다.

air gap 클러스터에서는 이미지를 클러스터 런타임에 직접 로드하거나 내부 레지스트리에 올려야 합니다. 매니페스트의 기본 이미지명은 `local/<server>:latest`입니다. 단일 노드 Docker 기반 환경에서는 `docker load`만으로 충분할 수 있지만, containerd 또는 멀티 노드 클러스터에서는 각 노드 런타임에 import하거나 내부 레지스트리 경로로 바꿔야 합니다.

`mcp-docker`는 기본 Kubernetes 매니페스트 대상에서 제외했습니다. Docker daemon socket 접근이 필요하고, containerd 기반 클러스터에서는 없는 경우가 많으며, host socket을 마운트하면 고권한 구성이 되기 때문입니다.

`mcp-rhapsody`도 Kubernetes와 Linux Docker 대상에서 제외합니다. Rhapsody 자동화는 Windows 설치, 사용자 세션, 라이선스, COM Automation, 로컬 CLI 도구에 의존하기 때문입니다.

## 서버별 문서

각 서버 폴더에는 영어 `README.md`와 한국어 `README.ko.md`가 있습니다.

- `mcp-office/README.md`, `mcp-office/README.ko.md`
- `mcp-filesystem/README.md`, `mcp-filesystem/README.ko.md`
- `mcp-git/README.md`, `mcp-git/README.ko.md`
- `mcp-shell/README.md`, `mcp-shell/README.ko.md`
- `mcp-dotnet/README.md`, `mcp-dotnet/README.ko.md`
- `mcp-mssql/README.md`, `mcp-mssql/README.ko.md`
- `mcp-hwp/README.md`, `mcp-hwp/README.ko.md`
- `mcp-kubernetes/README.md`, `mcp-kubernetes/README.ko.md`
- `mcp-docker/README.md`, `mcp-docker/README.ko.md`
- `mcp-prometheus/README.md`, `mcp-prometheus/README.ko.md`
- `mcp-postgresql/README.md`, `mcp-postgresql/README.ko.md`
- `mcp-gitlab/README.md`, `mcp-gitlab/README.ko.md`
- `mcp-jira/README.md`, `mcp-jira/README.ko.md`
- `mcp-loki/README.md`, `mcp-loki/README.ko.md`
- `mcp-rhapsody/README.md`, `mcp-rhapsody/README.ko.md`
