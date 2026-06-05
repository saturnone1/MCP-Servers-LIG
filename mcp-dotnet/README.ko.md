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

## 실행

```powershell
docker run --rm -p 8084:8080 -v ${PWD}:/workspace local/mcp-dotnet
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
| `build` | `dotnet build --no-restore`를 실행합니다. |
| `test` | `dotnet test --no-build`를 실행합니다. |
| `add_package` | `dotnet add package`를 실행하고, optional version을 지원합니다. |
| `format` | `dotnet format`을 실행합니다. |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | project path로 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `MCP_ENABLE_DOTNET_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 `add_package`, `format`을 막습니다. |
