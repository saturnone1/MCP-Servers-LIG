# mcp-autocad

영어 버전: [README.md](README.md)

AutoCAD 자동화를 위한 Windows 호스트 MCP 서버입니다. 널리 쓰이는 Autodesk 공식 AutoCAD desktop MCP 서버는 확인되지 않아, 오픈소스 AutoCAD MCP 프로젝트들이 많이 쓰는 방식인 Windows 사용자 세션 + AutoCAD COM Automation 패턴으로 구현했습니다.

## 원본 / 계보

- 공식 생태계 참고: Autodesk Platform Services MCP 서버 [`autodesk-platform-services/aps-mcp-server-nodejs`](https://github.com/autodesk-platform-services/aps-mcp-server-nodejs)가 있지만, AutoCAD desktop COM이 아니라 APS/API 중심입니다.
- 오픈소스 desktop 패턴 참고: [`daobataotie/CAD-MCP`](https://github.com/daobataotie/CAD-MCP), [`zh19980811/Easy-MCP-AutoCad`](https://github.com/zh19980811/Easy-MCP-AutoCad) 같은 CAD/AutoCAD MCP 프로젝트입니다.
- 로컬 구현: AutoCAD COM Automation을 사용하는 C#/.NET ASP.NET Core MCP 서버
- 실행 모델: Windows 사용자 세션, AutoCAD 설치, 로컬 라이선스, COM Automation

AutoCAD desktop 자동화는 Windows COM, GUI/session 상태, 라이선스에 의존하므로 Docker/Kubernetes 대상으로 두지 않습니다.

## 실행

```powershell
.\mcp-autocad\scripts\run-dev.ps1
```

airgap Windows 배포 폴더 생성:

```powershell
.\mcp-autocad\scripts\publish-win.ps1
```

publish 폴더에는 `McpAutoCad.exe`, `start.cmd`, `run.ps1`, `autocad.env`가 들어갑니다. `start.cmd`를 더블클릭하거나, `.\run.ps1`을 실행하거나, 환경 변수를 직접 설정한 뒤 `.\McpAutoCad.exe`를 바로 실행할 수 있습니다.

연결 주소:

- HTTP: `http://localhost:42196/mcp`
- SSE: `http://localhost:42196/sse`
- Health: `http://localhost:42196/healthz`

## 설정

| 변수 | 설명 |
| --- | --- |
| `AUTOCAD_EXE_PATH` | `acad.exe` 경로 힌트입니다. |
| `AUTOCAD_COM_PROGID` | COM ProgID입니다. 보통 `AutoCAD.Application`입니다. |
| `MCP_ALLOWED_DIRS` | drawing 접근을 허용할 Windows root 목록입니다. |
| `MCP_ENABLE_AUTOCAD_WRITES` | `false`로 설정하면 drawing 수정 tool을 막습니다. |

## COM 실행 정책

`config`, `detect_installations`, `/healthz`는 AutoCAD를 새로 실행하지 않는 안전 조회 경로입니다. COM 등록 여부와 이미 실행 중인 AutoCAD 세션만 확인합니다.

`open_drawing`, `active_drawing`, list/save/export 계열 tool은 실제 AutoCAD COM 세션이 필요합니다. 활성 AutoCAD가 없으면 현 정책상 AutoCAD가 새로 실행될 수 있습니다.

## 도구

| Tool | 기능 |
| --- | --- |
| `config` | 설정과 COM 탐지 상태를 반환합니다. |
| `detect_installations` | COM과 실행 파일 힌트를 찾습니다. |
| `open_drawing` | COM으로 DWG/DXF를 엽니다. |
| `active_drawing` | active drawing 정보를 반환합니다. |
| `list_layers` | active drawing의 layer를 나열합니다. |
| `list_model_space_entities` | model space entity를 나열합니다. |
| `list_blocks` | block definition을 나열합니다. |
| `list_block_references` | 삽입된 block reference를 나열합니다. |
| `list_texts` | text/mtext entity를 나열합니다. |
| `list_dimensions` | dimension entity를 나열합니다. |
| `list_curves` | line/polyline entity를 나열합니다. |
| `run_command` | AutoCAD command 문자열을 전송합니다. |
| `create_layer` | layer를 생성합니다. |
| `add_line` | model space에 line을 추가합니다. |
| `add_circle` | model space에 circle을 추가합니다. |
| `add_text` | model space에 single-line text를 추가합니다. |
| `save_drawing` | active drawing을 저장합니다. |
| `export_drawing` | AutoCAD `Export`로 active drawing을 export합니다. |
| `save_as_drawing` | target path에 drawing 사본을 저장합니다. |

