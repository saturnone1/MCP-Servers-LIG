# mcp-dotnet

영어 버전: [README.md](README.md)

.NET SDK 작업을 MCP tool로 제공하는 C# 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 참고 원본: `jongalloway/dotnet-mcp`
- 구현 방식: 원본 아이디어를 바탕으로 `dotnet` CLI를 호출하는 C# 래퍼로 재구현했습니다.
- 런타임 요구사항: 이미지 안에 .NET SDK가 설치되어 있고, 대상 프로젝트를 컨테이너에 마운트해야 합니다.
- trusted-local Docker 기본값: `dotnet add package`, `dotnet format` 같은 변경 작업을 허용합니다.

## 빌드

```powershell
docker build -t local/mcp-dotnet .
```

## Air Gap 추출

`local/mcp-dotnet:latest` 이미지를 `airgap/local-mcp-dotnet.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-dotnet -Port 8084
```

연결 주소:

- Streamable HTTP: `http://localhost:8084/mcp`
- Legacy SSE: `http://localhost:8084/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `sdk_info` | `dotnet --info`를 실행합니다. |
| `list_projects` | 지정 경로 아래 `.csproj`, `.fsproj`, `.vbproj`, `.sln` 파일을 찾습니다. |
| `restore` | `dotnet restore`를 실행합니다. |
| `build` | 필요하면 restore를 포함해 완전한 `dotnet build`를 실행합니다. |
| `test` | 필요하면 restore와 build를 포함해 완전한 `dotnet test`를 실행합니다. |
| `add_package` | `dotnet add package`를 실행하고, optional version을 지원합니다. |
| `format` | `dotnet format`을 실행합니다. |

## API 설명

명령 실행 tool은 `{ "exitCode": number, "stdout": string, "stderr": string }` 형태를 반환합니다.

| Tool | Arguments | 설명 |
| --- | --- | --- |
| `sdk_info` | 없음 | `/workspace`에서 `dotnet --info`를 실행합니다. |
| `list_projects` | `path` string = `.`, `limit` int = `2000` | 최대 100,000개 project/solution metadata를 반환합니다. |
| `restore` | `projectOrSolutionPath` string, `timeoutMs` int = `600000` | `dotnet restore`를 실행합니다. |
| `build` | `projectOrSolutionPath` string, `configuration` string = `Debug`, `timeoutMs` int = `600000` | 완전한 `dotnet build`를 실행합니다. |
| `test` | `projectOrSolutionPath` string, `configuration` string = `Debug`, `timeoutMs` int = `900000` | 완전한 `dotnet test`를 실행합니다. |
| `add_package` | `projectPath` string, `packageName` string, `version` string? = `null` | `dotnet add package`를 실행합니다. |
| `format` | `projectOrSolutionPath` string, `timeoutMs` int = `600000` | `dotnet format`을 실행하며 최대 24시간까지 지정할 수 있습니다. |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | project path로 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `MCP_ENABLE_DOTNET_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 `add_package`, `format`을 막습니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. Kubernetes 배포에서는 PVC를 `/workspace`에 마운트하고 `MCP_ALLOWED_DIRS=/workspace`, `MCP_ENABLE_DOTNET_WRITES=true`를 사용합니다. `sdk_info`, 프로젝트 탐색, build/test는 마운트된 소스로 동작합니다. air gap 클러스터에서 `restore`, `add_package`를 쓰려면 NuGet cache를 미리 넣거나 내부 NuGet feed가 필요합니다.
