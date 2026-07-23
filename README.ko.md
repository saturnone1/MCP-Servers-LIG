# MCP 원격 서버 번들

영어 버전: [README.md](README.md)

이 저장소는 범용 원격 MCP 서버 15개와 Windows 호스트 또는 데이터 처리 MCP 서버 5개를 담고 있습니다. 모든 서버는 C#/.NET ASP.NET Core 앱이며 `ModelContextProtocol.AspNetCore`를 사용합니다. 공통으로 Streamable HTTP는 `/mcp`, legacy SSE는 `/sse`와 `/message`를 제공하고 `/healthz`를 제공합니다.

이 이미지들은 신뢰할 수 있는 로컬 테스트용입니다. 기본값은 쓰기/실행 기능을 열어 두고, 컨테이너 내부 허용 경로도 `/`로 둡니다. 다만 호스트 파일시스템 접근은 Docker 볼륨으로 마운트한 범위에만 한정됩니다.

## 서버 목록

| 서버 | 포트 | 원본 / 계보 | 구현 방식 | 주요 기능 |
| --- | ---: | --- | --- | --- |
| `mcp-office` | 42180 | `iOfficeAI/OfficeCLI` | OfficeCLI를 이미지에 포함하고 legacy `.doc`용 `antiword` 추가 | Office 문서 검사/읽기, 텍스트 추출, 문서 생성, batch 편집, 렌더/내보내기, raw OfficeCLI |
| `mcp-filesystem` | 42181 | `mark3labs/mcp-filesystem-server` 보안 모델 | `System.IO` 기반 C# 재구현 | 파일 읽기/쓰기/복사/이동/삭제, stat, 디렉터리 목록/검색, 허용 root 처리 |
| `mcp-git` | 42182 | `modelcontextprotocol/servers` Git 서버 동작 | `git` CLI를 감싸는 C# 래퍼 | status, log, diff, show, branch list, blame, grep, init/add/commit/checkout |
| `mcp-shell` | 42183 | 신규 로컬 구현 | C# `ProcessStartInfo` 명령 실행기 | 컨테이너 내부 명령 실행, timeout, 출력 제한, 선택적 command/env allowlist |
| `mcp-dotnet` | 42184 | `jongalloway/dotnet-mcp`에서 아이디어 차용 | `dotnet` CLI를 감싸는 C# 래퍼 | SDK 정보, 프로젝트 탐색, restore/build/test, add package, format |
| `mcp-mssql` | 42185 | `little-fort/mcp-dotnet-mssql` 동작 기반 | `Microsoft.Data.SqlClient` 기반 C# SQL Server 도구 | DB/schema/table 목록, table describe, read query, non-query SQL |
| `mcp-hwp` | 42186 | 오픈 도구 기반 신규 구현 | `pyhwp`/`hwp5txt`, LibreOffice, ZIP/XML 파싱 | `.hwp`/`.hwpx` 텍스트 추출, 파일 검사, `txt/docx/pdf/odt` 변환 |
| `mcp-kubernetes` | 42187 | 신규 로컬 구현 | `kubectl` CLI를 감싸는 C# 래퍼 | cluster 정보, namespace, pod, log, deployment, YAML 적용/삭제/재시작/scale/생성 |
| `mcp-docker` | 42188 | 신규 로컬 구현 | Docker CLI와 Docker socket을 사용하는 C# 래퍼 | container, image, inspect, logs, run/start/stop/remove, pull/remove image |
| `mcp-prometheus` | 42189 | 신규 로컬 구현 | Prometheus HTTP API C# client | readiness, instant/range query, label, target, alert, series |
| `mcp-postgresql` | 42190 | 신규 로컬 구현 | `Npgsql` 기반 C# PostgreSQL 도구 | DB/schema/table 목록, table describe, read query, non-query SQL |
| `mcp-gitlab` | 42191 | 신규 로컬 구현 | GitLab REST API C# client | project, issue, merge request, repository file |
| `mcp-jira` | 42192 | 신규 로컬 구현 | Jira REST API C# client | JQL 검색, issue, comment, transition, project |
| `mcp-loki` | 42193 | 신규 로컬 구현 | Loki HTTP API C# client | LogQL query, 최근 로그 검색, label, series, index stats |
| `mcp-confluence` | 42198 | 신규 로컬 구현 | Confluence Data Center REST API v1 C# client | space, CQL content search, page, child page, page create/update/delete |
| `mcp-rhapsody` | 42194 | 신규 로컬 구현 | Rhapsody COM/CLI/file 자동화를 위한 Windows 호스트 C# 서버 | Rhapsody 탐지, 모델 파일 inspect, 설정된 CLI 실행 |
| `mcp-matlab` | 42195 | 공식 `matlab/matlab-mcp-core-server` 계보 | MATLAB CLI/COM을 감싸고 공식 MCP bridge hook을 둔 Windows 호스트 C# 서버 | MATLAB 탐지, batch/script 실행, COM eval, workspace 요약 |
| `mcp-autocad` | 42196 | 오픈소스 AutoCAD MCP COM 자동화 패턴 | AutoCAD COM을 감싸는 Windows 호스트 C# 서버 | drawing 열기, layer/entity 조회, command 전송, layer/line 생성, 저장 |
| `mcp-solidworks` | 42197 | 오픈소스 SolidWorks MCP COM 자동화 패턴 | SolidWorks COM을 감싸는 Windows 호스트 C# 서버 | CAD 문서 열기, feature/component 조회, mass property, rebuild/save/export |

## 연결 주소

Docker 이미지는 컨테이너 내부 `8080` 포트에서 실행됩니다. Windows-host desktop 서버는 표에 적힌 localhost 포트에서 직접 실행됩니다. smoke test에서 사용하는 호스트 포트 배치는 다음과 같습니다.

| 서버 | Streamable HTTP | Legacy SSE |
| --- | --- | --- |
| `mcp-office` | `http://localhost:42180/mcp` | `http://localhost:42180/sse` |
| `mcp-filesystem` | `http://localhost:42181/mcp` | `http://localhost:42181/sse` |
| `mcp-git` | `http://localhost:42182/mcp` | `http://localhost:42182/sse` |
| `mcp-shell` | `http://localhost:42183/mcp` | `http://localhost:42183/sse` |
| `mcp-dotnet` | `http://localhost:42184/mcp` | `http://localhost:42184/sse` |
| `mcp-mssql` | `http://localhost:42185/mcp` | `http://localhost:42185/sse` |
| `mcp-hwp` | `http://localhost:42186/mcp` | `http://localhost:42186/sse` |
| `mcp-kubernetes` | `http://localhost:42187/mcp` | `http://localhost:42187/sse` |
| `mcp-docker` | `http://localhost:42188/mcp` | `http://localhost:42188/sse` |
| `mcp-prometheus` | `http://localhost:42189/mcp` | `http://localhost:42189/sse` |
| `mcp-postgresql` | `http://localhost:42190/mcp` | `http://localhost:42190/sse` |
| `mcp-gitlab` | `http://localhost:42191/mcp` | `http://localhost:42191/sse` |
| `mcp-jira` | `http://localhost:42192/mcp` | `http://localhost:42192/sse` |
| `mcp-loki` | `http://localhost:42193/mcp` | `http://localhost:42193/sse` |
| `mcp-confluence` | `http://localhost:42198/mcp` | `http://localhost:42198/sse` |
| `mcp-rhapsody` | `http://localhost:42194/mcp` | `http://localhost:42194/sse` |
| `mcp-matlab` | `http://localhost:42195/mcp` | `http://localhost:42195/sse` |
| `mcp-autocad` | `http://localhost:42196/mcp` | `http://localhost:42196/sse` |
| `mcp-solidworks` | `http://localhost:42197/mcp` | `http://localhost:42197/sse` |

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
$servers = 'mcp-office','mcp-filesystem','mcp-git','mcp-shell','mcp-mssql','mcp-dotnet','mcp-hwp','mcp-kubernetes','mcp-docker','mcp-prometheus','mcp-postgresql','mcp-gitlab','mcp-jira','mcp-loki','mcp-confluence'
foreach ($server in $servers) {
  docker build -t "local/$server" $server
}
```

런타임 이미지는 인터넷 없이 실행되는 것을 목표로 합니다. NuGet restore, apt/pip 설치, upstream 다운로드는 빌드 시점에 수행됩니다.

`mcp-rhapsody`는 Docker 빌드 목록에 포함하지 않습니다. Windows 호스트 실행 패키지로 publish합니다.

```powershell
.\mcp-rhapsody\scripts\publish-win.ps1
```

MATLAB, AutoCAD, SolidWorks MCP 서버도 Windows 호스트 실행 패키지로 publish합니다.

```powershell
.\mcp-matlab\scripts\publish-win.ps1
.\mcp-autocad\scripts\publish-win.ps1
.\mcp-solidworks\scripts\publish-win.ps1
```

MATLAB의 경우 MathWorks 공식 MCP server binary까지 air-gap 패키지에 넣으려면 publish 전에 `.\mcp-matlab\scripts\download-official-mcp.ps1`을 실행합니다. 그러면 publish 폴더의 `official/` 아래로 같이 복사됩니다.

Windows-host desktop 서버를 한 번에 publish하려면 다음을 사용합니다.

```powershell
.\scripts\publish-windows-host.ps1 -Zip
```

출력 폴더에는 Windows `.exe`, `run.ps1`, 더블클릭용 `start.cmd`, 수정 가능한 `.env` 파일이 들어갑니다. 예시는 다음과 같습니다.

```text
windows-host-publish\mcp-matlab-win-x64\McpMatlab.exe
windows-host-publish\mcp-matlab-win-x64\start.cmd
windows-host-publish\mcp-matlab-win-x64\run.ps1
```

기본 번들 publish는 framework-dependent `win-x64` 서버들과 공유 .NET/ASP.NET Core 런타임 1벌을 `mcp-bundle\dotnet` 아래에 함께 넣습니다. 대상 PC에 .NET 10 또는 ASP.NET Core 10을 별도로 설치하지 않아도 됩니다. 이미 설치된 런타임에 의존하려면 `-BundleDotnetRuntime $false`, 서버마다 런타임을 각각 포함하는 큰 번들이 필요하면 `-SelfContained $true`를 넘기면 됩니다.

## Windows 통합 EXE 번들

Docker가 기본 실행 방식이던 MCP 서버까지 모두 Windows `.exe`로 publish해서 `mcp-manager`로 한 번에 제어할 수도 있습니다. 기존 Dockerfile, airgap tar, Kubernetes YAML은 그대로 유지하고, Windows 로컬 실행용 번들을 추가로 만드는 방식입니다.

```powershell
.\scripts\publish-mcp-bundle.ps1 -Zip
```

출력은 `mcp-bundle` 폴더입니다.

```text
mcp-bundle\McpManager.exe
mcp-bundle\servers.json
mcp-bundle\start-all.cmd
mcp-bundle\stop-all.cmd
mcp-bundle\status.cmd
mcp-bundle\urls.cmd
mcp-bundle\mcp-office-win-x64\McpOffice.exe
mcp-bundle\mcp-filesystem-win-x64\McpFilesystem.exe
mcp-bundle\mcp-git-win-x64\McpGit.exe
...
mcp-bundle\mcp-solidworks-win-x64\McpSolidWorks.exe
```

이 번들의 `servers.json`은 19개 서버를 모두 `process` 방식으로 등록합니다. 따라서 `McpManager.exe start all`은 Docker를 호출하지 않고 각 서버의 `Mcp*.exe`를 직접 실행합니다.

번들의 `<server>.env`는 기본값이며 수정 가능한 사용자별 override는 `%LOCALAPPDATA%\LIG AI MCP\.mcp-manager\env`에 저장됩니다. `edit-env-mcp-jira.cmd` 또는 Manager 화면에서 수정한 뒤 서버를 재시작하세요. Manager는 번들 루트와 서버 폴더의 기본 env, `servers.json`의 `envFiles`를 먼저 읽고 사용자별 override를 마지막에 적용합니다.

기존 번들에 새 manager만 덮어쓴 경우에는 `sync-env-files.ps1`을 한 번 실행하면 `servers.json`의 env 값이 서버별 `.env` 파일로 분리되고 `edit-env-*.cmd`가 생성됩니다.

`mcp-bundle\McpManager.exe`를 그냥 더블클릭하면 콘솔 메뉴가 열립니다. 메뉴에서 전체 시작/종료/재시작, 상태 확인, URL 확인, 서버별 시작/종료, 로그 확인을 선택할 수 있습니다.

대시보드에서 서버를 선택하고 `P`를 누르면 자동실행을 등록하거나 해제합니다. 등록된 서버는 `[A]`로 표시되고 다음 번 번들 실행 시 자동으로 시작됩니다. 목록은 번들 루트의 `autostart.json`에 보존되며 메인 대시보드를 닫으면 자동 시작된 서버도 함께 종료됩니다.

```powershell
.\mcp-bundle\McpManager.exe list all
.\mcp-bundle\McpManager.exe start mcp-filesystem
.\mcp-bundle\McpManager.exe status all
.\mcp-bundle\McpManager.exe urls all
.\mcp-bundle\McpManager.exe stop all
.\mcp-bundle\LIG-AI-MCP.cmd env mcp-filesystem
.\mcp-bundle\LIG-AI-MCP.cmd set-env mcp-filesystem MCP_ALLOWED_DIRS "*"
.\mcp-bundle\LIG-AI-MCP.cmd remove-env mcp-filesystem MCP_ALLOWED_DIRS
.\mcp-bundle\LIG-AI-MCP.cmd autostart enable mcp-filesystem
.\mcp-bundle\LIG-AI-MCP.cmd autostart list
```

대시보드에서는 서버를 선택한 뒤 `E`를 누르면 텍스트 편집기 없이 환경변수를 수정할 수 있습니다. `A` 추가, `Enter` 수정, `D` 삭제, `N` 메모장 열기, `B` 뒤로가기를 지원합니다. 환경변수를 바꾼 뒤에는 해당 서버를 재시작해야 적용됩니다.

더블클릭 실행용 `start-all.cmd`, `stop-all.cmd`, `status.cmd`와 서버별 `start-mcp-*.cmd`, `stop-mcp-*.cmd`도 함께 생성됩니다. 이 런처들은 `runtime-env.cmd`를 통해 번들 내부 공유 런타임을 자동으로 사용하므로 대상 Windows PC에 전역 .NET 설치가 필요하지 않습니다.

번들 구조와 외부 CLI 준비 상태는 다음 스크립트로 확인합니다.

```powershell
.\scripts\test-mcp-bundle.ps1
```

Windows 설치 파일은 다음 명령으로 생성합니다.

```powershell
.\scripts\build-installer.ps1
```

기본 버전은 `installer\VERSION`에서 읽으며, 의도적으로 다른 릴리스를 만들 때만 `-Version`으로 덮어씁니다. 정식 서명 빌드는 `-CertificateThumbprint <thumbprint>`를 전달하거나 `LIG_SIGNING_CERT_THUMBPRINT` 환경 변수를 설정하면 제품 실행 파일, 내장 MSI 및 최종 Setup을 모두 서명합니다.

사용자에게 제공되는 설치 파일은 `installer\output`의 `Setup.exe` 하나뿐이며 MSI는 그 안에 내장되는 빌드 재료로만 사용합니다. Setup 자체가 먼저 UAC 관리자 권한을 요청하고 명시적인 설치 진행 창과 최상단 완료 창을 표시합니다. 앱 목록과 시작 메뉴에는 관리자 권한을 자체 요청하고 실행 중 프로세스를 정리한 뒤 제거 진행 창과 완료 창을 표시하는 전용 Uninstaller가 등록됩니다. 사용자는 `mcp-bundle`, MSI, ZIP 또는 별도 .NET/ASP.NET Core 런타임 설치 파일이 필요하지 않습니다. 설치본은 MCP 서버 19개, 공유 런타임, 제품 아이콘을 내장한 self-contained Manager, 시작 메뉴 및 바탕화면 바로가기와 실패 시 복구되는 업그레이드를 포함합니다. `McpManager.exe`는 실행할 때마다 관리자 권한을 요청하고 여기서 시작한 서버는 권한을 상속하지만, 개별 MCP 서버 EXE는 LLM이 직접 실행할 수 있도록 승격을 강제하지 않습니다. 바로가기는 `McpManager.exe`를 직접 실행하므로 작업표시줄에도 제품 아이콘이 표시됩니다. 프로그램은 Program Files에 컴퓨터 단위로 설치되고, 수정 가능한 설정·로그·PID는 `%LOCALAPPDATA%\LIG AI MCP\.mcp-manager`에 저장합니다. 인증서를 지정하지 않으면 설치 파일은 서명되지 않으며 빌드가 명확한 경고를 출력합니다. 자세한 내용은 `installer\README.ko.md`를 참고하세요.

주의할 점은 서버 실행 파일 자체는 포함되지만, 일부 tool이 호출하는 외부 프로그램은 대상 PC에 있어야 한다는 점입니다. Dockerfile에서 `apt-get`, `curl`, `pip`로 설치하던 항목은 Windows exe 번들에 자동으로 포함되지 않습니다.

| 서버 | Windows exe 번들 상태 | 추가 필요 항목 |
| --- | --- | --- |
| `mcp-filesystem` | 자체 동작 | 없음 |
| `mcp-mssql`, `mcp-postgresql` | 자체 동작 | 실제 DB 연결 문자열 |
| `mcp-prometheus`, `mcp-gitlab`, `mcp-jira`, `mcp-loki`, `mcp-confluence` | 자체 동작 | 실제 API URL/토큰 |
| `mcp-shell` | 자체 동작 | 실행할 명령이 Windows에 존재해야 함 |
| `mcp-git` | 서버는 자체 동작 | `git.exe` |
| `mcp-dotnet` | 서버는 번들 런타임으로 동작 | 대상 PC의 외부 .NET SDK/CLI. 필요하면 `MCP_DOTNET_CLI_PATH`로 명시할 수 있으며, 프로젝트의 `global.json`과 대상 프레임워크에 따라 .NET 8/9/10 SDK를 선택합니다. |
| `mcp-kubernetes` | 서버는 자체 동작 | `kubectl.exe`, kubeconfig 또는 in-cluster 대체 환경 |
| `mcp-docker` | 서버는 자체 동작 | Docker CLI와 Docker Desktop/daemon |
| `mcp-office` | `officecli.exe`를 번들에 동봉 | legacy `.doc`용 `antiword`는 선택 사항. 없으면 OfficeCLI로 fallback |
| `mcp-hwp` | `.hwpx`와 기본 `.hwp` 텍스트 추출은 내장 파서로 처리 | 고급 `.hwp` fallback은 선택적 `hwp5txt`, `docx/pdf/odt` 변환은 LibreOffice `soffice` |
| `mcp-rhapsody`, `mcp-matlab`, `mcp-autocad`, `mcp-solidworks` | 서버는 자체 동작 | 해당 상용 프로그램, COM/CLI, 라이선스 |

Office 서버는 `mcp-office\vendor\officecli`에 받아 둔 OfficeCLI Windows binary를 publish 시 `tools/officecli.exe`로 함께 복사합니다. vendor에 없으면 `publish-mcp-bundle.ps1`이 `mcp-office\scripts\download-officecli.ps1`을 호출해 내려받습니다.

MATLAB 서버는 `mcp-matlab\vendor\official`에 받아 둔 MathWorks 공식 MCP binary를 publish 시 `official/` 폴더로 함께 복사합니다.

기존 전체 번들 안에 넣을 AutoCAD 교체 폴더만 publish하려면 다음을 사용합니다.

```powershell
.\scripts\publish-autocad-bundle-patch.ps1 -Zip
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
mcp-confluence\airgap\local-mcp-confluence.tar
```

필요한 `airgap` 폴더 또는 tar 파일을 air gap PC로 옮긴 뒤 `docker load -i <tar-file>`로 로드하면 됩니다. 각 서버 폴더의 `airgap/README.ko.md`에는 해당 서버의 정확한 load/run 명령이 들어 있습니다. tar archive는 Git에 커밋되지 않도록 제외했습니다.

## 검증

```powershell
.\tests\verify-priority.ps1 -SkipBuild -SkipImagePull
```

우선순위 검증 스크립트는 Docker MCP smoke, 외부 API mock 호출, PostgreSQL fixture, SQL Server fixture, Windows-host Rhapsody MCP smoke를 순서대로 실행합니다. Rhapsody가 설치된 Windows PC에서는 `-RhapsodyProjectPath "C:\path\model.rpyx"`를 추가하면 COM read smoke까지 실행하고, 모델 수정/저장이 안전할 때만 `-RunRhapsodyWriteSmoke`를 추가합니다.

Windows-host MATLAB, AutoCAD, SolidWorks MCP 서버는 다음 smoke로 확인합니다.

```powershell
.\tests\desktop-host-smoke.ps1
```

Docker 서버만 빠르게 확인하려면 다음을 사용합니다.

```powershell
.\tests\mcp-smoke.ps1 -SkipBuild
```

Docker smoke test는 컨테이너를 재시작한 뒤 `/healthz`, SSE, MCP tool 목록, 대표 tool 호출을 확인합니다. Prometheus, GitLab, Jira, Loki는 로컬 mock HTTP API를 상대로 실제 HTTP 호출까지 확인합니다. PostgreSQL과 SQL Server의 실제 DB 호출은 `tests/`의 fixture 스크립트에서 확인합니다.

## LIG AI MCP

`mcp-manager`는 Docker MCP 서버와 Windows-host MCP 서버를 한 곳에서 시작/중지/상태확인/log 확인하는 CLI입니다.

개발 상태에서:

```powershell
.\mcp-manager\scripts\run.ps1 list all
.\mcp-manager\scripts\run.ps1 start all
.\mcp-manager\scripts\run.ps1 status all
.\mcp-manager\scripts\run.ps1 stop all
```

Windows native manager 실행 파일 생성:

```powershell
.\mcp-manager\scripts\publish-win.ps1
```

publish 폴더에는 `McpManager.exe`, `LIG-AI-MCP.cmd`, `mcp-manager.cmd`, `start-all.cmd`, `stop-all.cmd`, `status.cmd`, `servers.json`가 들어갑니다.

번들에는 `fonts\NotoSansKR[wght].ttf`와 `install-fonts.cmd`도 포함됩니다. `install-fonts.cmd`를 한 번 실행하면 현재 Windows 사용자 폰트로 Noto Sans KR이 설치됩니다. 이후 터미널을 재시작하고 Windows Terminal/CMD 설정에서 해당 폰트를 선택하면 한글 UI 폰트를 맞출 수 있습니다. 콘솔 앱 자체가 터미널 폰트를 안정적으로 강제하는 것은 Windows 콘솔 제약 때문에 권장하지 않습니다.

smoke 호출 없이 15개 Docker 서버를 모두 실행하려면 다음 스크립트를 사용합니다.

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

Linux 컨테이너 안에서 MCP 클라이언트가 넘긴 Windows 호스트 경로를 쓰려면 호스트 드라이브를 Docker로 마운트하고 `MCP_PATH_MAPPINGS`에 등록해야 합니다. 제공된 helper는 준비된 Windows 드라이브를 모두 자동 처리하고 포트를 localhost에만 공개합니다.

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-filesystem -Port 42181
```

같은 방식의 경로 매핑은 Office, filesystem, Git, shell, .NET, HWP, Kubernetes처럼 파일 경로를 받는 서버에서 지원합니다. MCP 매니저 설정에서 `mountHostDrives`가 활성화된 서버는 실행 시 준비된 Windows 드라이브(C:, D:, E: 등)를 `/host/drives/<문자>`에 자동 마운트하고 경로 매핑을 구성합니다. Windows 호스트 프로세스에서는 `MCP_ALLOWED_DIRS=*`가 현재 연결된 모든 드라이브 루트를 뜻합니다. 운영체제 계정 권한이나 Docker Desktop의 드라이브 공유 정책은 그대로 적용됩니다.

## Kubernetes 배포

Linux Kubernetes workload로 자연스럽게 실행할 수 있는 MCP 서버에만 Kubernetes 매니페스트를 제공합니다.

- 포함: `mcp-filesystem`, `mcp-git`, `mcp-dotnet`, `mcp-kubernetes`, `mcp-prometheus`, `mcp-postgresql`, `mcp-gitlab`, `mcp-jira`, `mcp-loki`, `mcp-confluence`
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

`mcp-matlab`, `mcp-autocad`, `mcp-solidworks`도 같은 desktop automation 이유로 제외합니다. 설치된 Windows desktop 앱, 사용자/session context, 라이선스, COM 또는 로컬 CLI 자동화가 필요합니다.

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
- `mcp-confluence/README.md`, `mcp-confluence/README.ko.md`
- `mcp-rhapsody/README.md`, `mcp-rhapsody/README.ko.md`
- `mcp-matlab/README.md`, `mcp-matlab/README.ko.md`
- `mcp-autocad/README.md`, `mcp-autocad/README.ko.md`
- `mcp-solidworks/README.md`, `mcp-solidworks/README.ko.md`

