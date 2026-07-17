# mcp-matlab

영어 버전: [README.md](README.md)

MATLAB/Simulink 자동화를 위한 Windows 호스트 MCP 서버입니다. MATLAB은 MathWorks 공식 MCP Core Server가 있으므로, 이 서버는 그 방향과 맞추되 `matlab -batch`와 MATLAB COM Automation을 직접 호출하는 C# tool도 제공합니다.

## 원본 / 계보

- 공식 upstream 참고: [`matlab/matlab-mcp-core-server`](https://github.com/matlab/matlab-mcp-core-server)
- 로컬 구현: C#/.NET ASP.NET Core MCP 서버, Streamable HTTP와 legacy SSE 지원
- 실행 모델: Windows 사용자 세션, MATLAB 설치, 로컬 라이선스, 선택적 COM Automation

MATLAB GUI, 라이선스, COM, Simulink workflow는 보통 호스트 세션에 의존하므로 Docker/Kubernetes 대상으로 두지 않습니다.

## 실행

개발 실행:

```powershell
.\mcp-matlab\scripts\run-dev.ps1
```

airgap Windows 배포 폴더 생성:

```powershell
.\mcp-matlab\scripts\publish-win.ps1
```

MathWorks 공식 MCP 서버까지 publish 폴더에 동봉하려면, 인터넷이 되는 PC에서 먼저 내려받습니다.

```powershell
.\mcp-matlab\scripts\download-official-mcp.ps1
.\mcp-matlab\scripts\publish-win.ps1
```

다운로드 스크립트는 공식 binary를 `mcp-matlab/vendor/official/` 아래에 저장합니다. `publish-win.ps1`은 이 폴더를 `publish/official/`로 복사하고, `run.ps1`은 `MATLAB_MCP_CORE_SERVER_PATH`가 비어 있으면 `official/matlab-mcp*.exe`를 자동 탐지합니다.

publish 폴더에는 다음 파일들이 들어갑니다.

- `McpMatlab.exe`: Windows native 실행 파일
- `start.cmd`: 더블클릭 실행용
- `run.ps1`: PowerShell 실행용
- `matlab.env`: 수정 가능한 설정 파일
- `official/`: 선택적으로 동봉된 MathWorks 공식 MCP binary

publish 폴더에서 실행:

```powershell
.\run.ps1
```

`start.cmd`를 더블클릭하거나, 환경 변수를 직접 설정한 뒤 `.\McpMatlab.exe`를 바로 실행할 수도 있습니다.

연결 주소:

- HTTP: `http://localhost:42195/mcp`
- SSE: `http://localhost:42195/sse`
- Health: `http://localhost:42195/healthz`

## 설정

개발 환경에서는 `config/matlab.env.example`을 `config/matlab.env`로 복사해 수정합니다. publish 폴더에서는 `matlab.env`를 수정합니다.

| 변수 | 설명 |
| --- | --- |
| `MATLAB_ROOT` | MATLAB root 디렉터리입니다. |
| `MATLAB_EXE_PATH` | `matlab.exe` 경로를 명시합니다. |
| `MATLAB_COM_PROGID` | COM ProgID입니다. 보통 `Matlab.Application`입니다. |
| `MATLAB_MCP_CORE_SERVER_PATH` | MathWorks 공식 MCP Core Server 실행 파일/스크립트 경로입니다. |
| `MATLAB_MCP_CORE_SERVER_ARGS` | MathWorks 공식 MCP Core Server에 전달할 추가 인자입니다. |
| `MCP_ALLOWED_DIRS` | script 파일 접근을 허용할 Windows root 목록입니다. |
| `MCP_ENABLE_MATLAB_WRITES` | `false`로 설정하면 batch/script 실행, COM eval, 공식 MCP 호출, Simulink load/set/simulate/build를 막습니다. |

## 실행 정책

`config`, `detect_installations`, `/healthz`는 MATLAB을 새로 실행하지 않는 안전 조회 경로입니다. COM 등록 여부, 이미 실행 중인 MATLAB 세션, 실행 파일 후보, 공식 MCP 경로만 확인합니다.

`run_batch`, `run_script`, Simulink batch 계열 tool은 `matlab.exe`를 실행합니다. `eval_command`, `list_workspace`는 MATLAB COM 세션이 필요하며, 활성 MATLAB이 없으면 현 정책상 MATLAB이 새로 실행될 수 있습니다. 공식 MCP bridge tool은 호출마다 설정된 MathWorks 공식 MCP child process를 실행합니다.

## 도구

| Tool | 기능 |
| --- | --- |
| `config` | 설정, 탐지 상태, 공식 MCP 경로 상태를 반환합니다. |
| `detect_installations` | env, PATH, 일반 설치 폴더에서 MATLAB을 찾습니다. |
| `run_batch` | `matlab -batch "<command>"`를 실행합니다. |
| `run_script` | `.m` 파일을 `matlab -batch run('path')`로 실행합니다. |
| `eval_command` | MATLAB COM Automation으로 코드를 평가합니다. |
| `list_workspace` | COM으로 `whos`를 실행해 workspace 요약을 반환합니다. |
| `official_mcp_initialize` | MathWorks 공식 MCP 서버를 stdio로 실행하고 initialize 응답을 반환합니다. |
| `official_mcp_tools_list` | MathWorks 공식 MCP 서버의 tool 목록을 조회합니다. |
| `official_mcp_tool_call` | bridge를 통해 MathWorks 공식 MCP 서버의 tool을 호출합니다. |
| `official_mcp_raw_request` | initialize 이후 MathWorks 공식 MCP 서버에 raw JSON-RPC 요청을 보냅니다. |
| `simulink_load_system` | Simulink model/system을 로드합니다. |
| `simulink_find_system` | `find_system`을 실행하고 JSON 출력을 반환합니다. |
| `get_param` | MATLAB/Simulink parameter를 읽습니다. |
| `set_param` | MATLAB/Simulink parameter를 설정합니다. |
| `simulink_simulate` | `sim`을 실행합니다. |
| `simulink_build` | `slbuild`를 실행합니다. |

## 공식 MATLAB MCP Bridge

`MATLAB_MCP_CORE_SERVER_PATH`에 MathWorks 공식 MCP server binary 경로를 지정하거나, publish 폴더의 `official/` 아래에 binary를 넣으면 이 서버가 공식 서버를 stdio child process로 띄워 generic bridge tool로 연결합니다.

```powershell
MATLAB_MCP_CORE_SERVER_PATH=C:\tools\matlab-mcp-core-server-windows-x64.exe
```

먼저 `official_mcp_tools_list`로 공식 upstream tool 목록을 확인하고, `official_mcp_tool_call`에 upstream tool 이름과 arguments를 넘겨 호출합니다. bridge는 호출마다 짧게 공식 MCP child process를 실행하고 initialize 후 요청을 전달한 뒤 JSON-RPC 응답을 반환합니다.

air-gap 배포 시에는 publish 폴더 전체를 옮기면 됩니다. 이 폴더에는 C# HTTP/SSE MCP 서버, 선택적으로 `official/` 아래에 포함된 MathWorks 공식 MCP binary, 그리고 `matlab.env`가 함께 들어갑니다.

## 참고

MATLAB이 설치되어 있지 않아도 서버는 뜹니다. 이 경우 `config`에서 실행 파일/COM 미탐지 상태를 보여주고, MATLAB이 필요한 tool은 명확한 설정 오류를 반환합니다.

