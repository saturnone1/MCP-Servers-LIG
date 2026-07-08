# mcp-solidworks

영어 버전: [README.md](README.md)

SolidWorks 자동화를 위한 Windows 호스트 MCP 서버입니다. Dassault/SolidWorks 공식 MCP 서버는 확인되지 않아, 오픈소스 SolidWorks MCP 서버들이 주로 쓰는 SolidWorks COM API 패턴을 기준으로 구현했습니다.

## 원본 / 계보

- 오픈소스 참고 패턴: SolidWorks MCP 서버들은 보통 SolidWorks COM API를 감쌉니다. 예시는 [`vespo92/SolidworksMCP-TS`](https://github.com/vespo92/SolidworksMCP-TS), [`tylerstoltz/SW_MCP`](https://github.com/tylerstoltz/SW_MCP), [`eyfel/mcp-server-solidworks`](https://github.com/eyfel/mcp-server-solidworks)입니다.
- 로컬 구현: late-bound COM Automation을 사용하는 C#/.NET ASP.NET Core MCP 서버
- 실행 모델: Windows 사용자 세션, SolidWorks 설치, 로컬 라이선스, COM Automation

SolidWorks 자동화는 Windows desktop, GUI/session 상태, 라이선스, COM에 의존하므로 Docker/Kubernetes 대상으로 두지 않습니다.

## 실행

```powershell
.\mcp-solidworks\scripts\run-dev.ps1
```

airgap Windows 배포 폴더 생성:

```powershell
.\mcp-solidworks\scripts\publish-win.ps1
```

publish 폴더에는 `McpSolidWorks.exe`, `start.cmd`, `run.ps1`, `solidworks.env`가 들어갑니다. `start.cmd`를 더블클릭하거나, `.\run.ps1`을 실행하거나, 환경 변수를 직접 설정한 뒤 `.\McpSolidWorks.exe`를 바로 실행할 수 있습니다.

연결 주소:

- HTTP: `http://localhost:42197/mcp`
- SSE: `http://localhost:42197/sse`
- Health: `http://localhost:42197/healthz`

## 설정

| 변수 | 설명 |
| --- | --- |
| `SOLIDWORKS_EXE_PATH` | `SLDWORKS.exe` 경로 힌트입니다. |
| `SOLIDWORKS_COM_PROGID` | COM ProgID입니다. 보통 `SldWorks.Application`입니다. |
| `MCP_ALLOWED_DIRS` | CAD 파일과 export 접근을 허용할 Windows root 목록입니다. |
| `MCP_ENABLE_SOLIDWORKS_WRITES` | `false`로 설정하면 수정/export tool을 막습니다. |

## COM 실행 정책

`config`, `detect_installations`, `/healthz`는 SolidWorks를 새로 실행하지 않는 안전 조회 경로입니다. COM 등록 여부와 이미 실행 중인 SolidWorks 세션만 확인합니다.

`open_document`, `active_document`, list/save/export 계열 tool은 실제 SolidWorks COM 세션이 필요합니다. 활성 SolidWorks가 없으면 현 정책상 SolidWorks가 새로 실행될 수 있습니다.

## 도구

| Tool | 기능 |
| --- | --- |
| `config` | 설정과 COM 탐지 상태를 반환합니다. |
| `detect_installations` | COM과 실행 파일 힌트를 찾습니다. |
| `open_document` | part, assembly, drawing 파일을 엽니다. |
| `active_document` | active document 정보를 반환합니다. |
| `list_features` | top-level feature를 나열합니다. |
| `list_components` | assembly component를 나열합니다. |
| `list_configurations` | configuration을 나열합니다. |
| `list_equations` | equation을 나열합니다. |
| `list_custom_properties` | custom property를 나열합니다. |
| `set_custom_property` | custom property를 설정하거나 추가합니다. |
| `get_mass_properties` | 가능한 경우 mass/volume/surface area를 반환합니다. |
| `rebuild_model` | active model을 rebuild합니다. |
| `save_document` | active document를 저장합니다. |
| `export_document` | SolidWorks `SaveAs`로 STEP/STL/PDF 등 지원 형식으로 export합니다. |
| `export_step` | STEP으로 export합니다. |
| `export_stl` | STL로 export합니다. |
| `export_pdf` | PDF로 export합니다. |
| `close_active_document` | active document를 닫습니다. |

