# mcp-rhapsody

영어 버전: [README.md](README.md)

IBM Engineering Systems Design Rhapsody 자동화를 위한 Windows 호스트 MCP 서버입니다. Rhapsody 자동화는 보통 Windows 설치, 사용자 세션, 라이선스, COM Automation, 로컬 CLI 도구에 의존하므로 Docker/Kubernetes 서버로 만들지 않습니다.

## 실행

개발 PC에서 실행:

```powershell
.\mcp-rhapsody\scripts\run-dev.ps1
```

airgap Windows 배포 폴더 생성:

```powershell
.\mcp-rhapsody\scripts\publish-win.ps1
```

대상 Windows PC에서 실행:

```powershell
.\run.ps1
```

연결 주소:

- HTTP: `http://localhost:8094/mcp`
- SSE: `http://localhost:8094/sse`
- Health: `http://localhost:8094/healthz`

## 설정

개발 환경에서는 `config/rhapsody.env.example`을 `config/rhapsody.env`로 복사해 수정합니다. publish된 폴더에서는 `rhapsody.env`를 수정합니다.

| 변수 | 설명 |
| --- | --- |
| `RHAPSODY_INSTALL_DIR` | Rhapsody 설치 폴더를 명시합니다. |
| `RHAPSODY_EXE_PATH` | Rhapsody 실행 파일 경로를 명시합니다. |
| `RHAPSODY_CLI_PATH` | Rhapsody CLI 경로를 명시합니다. |
| `RHAPSODY_COM_PROGID` | Rhapsody COM ProgID를 명시합니다. |
| `MCP_ALLOWED_DIRS` | project/model 파일 접근을 허용할 Windows root입니다. |
| `MCP_ENABLE_RHAPSODY_WRITES` | `false`로 설정하면 향후 write tool을 막습니다. |
| `MCP_ENABLE_RHAPSODY_CLI` | `false`로 설정하면 raw CLI 실행을 막습니다. |

## 도구

| Tool | 기능 |
| --- | --- |
| `config` | 설정값과 Rhapsody 연동 탐지 상태를 반환합니다. |
| `detect_installations` | 일반 설치 경로, PATH, COM 힌트를 탐색합니다. |
| `inspect_project_file` | Rhapsody를 열지 않고 `.rpy`, `.rpyx`, `.sbs`, `.cls`, `.omd` 파일의 기본 정보를 읽습니다. |
| `run_rhapsody_cli` | 설정된 Rhapsody CLI에 raw argument를 전달해 실행합니다. |
| `open_project` | COM Automation으로 Rhapsody project를 엽니다. |
| `current_project` | 현재 active project 정보를 반환합니다. |
| `save_project` | 현재 active project를 저장합니다. |
| `list_packages` | active project의 package 목록을 반환합니다. |
| `list_classes` | active project의 class 목록을 반환합니다. |
| `list_interfaces` | active project의 interface/interface block 목록을 반환합니다. |
| `list_statecharts` | active project의 statechart/state machine 목록을 반환합니다. |
| `get_element` | 이름/full path와 metaclass로 element를 찾습니다. |
| `search_elements` | 이름/full path 기준으로 element를 검색합니다. |
| `create_package` | package를 생성합니다. |
| `create_class` | class를 생성합니다. |
| `create_interface` | interface를 생성합니다. |
| `set_element_property` | element property 값을 설정합니다. |
| `set_element_tag` | element tag 값을 설정합니다. |

## 참고

Rhapsody가 설치되어 있지 않아도 서버는 뜹니다. 이 경우 `config`에서 COM/CLI 미탐지 상태를 보여주고, 파일 inspect는 계속 동작하며, COM/CLI tool은 명확한 설정 오류를 반환합니다.

COM 기반 tool은 Rhapsody가 설치된 Windows 사용자 세션에서 실행해야 합니다. IBM Rhapsody API 문서 기준으로 COM API와 Java API는 거의 같은 object/method 이름을 사용하므로, 서버는 `activeProject`, `openProject`, `findNestedElement`, `getNestedElements`, `addClass`, `addPackage`, `save` 같은 호출을 late-binding으로 사용합니다.

## 테스트

Rhapsody가 없는 개발 PC에서는 서버 기동, MCP 초기화, tool 등록, `config` 호출까지만 확인합니다.

```powershell
.\tests\rhapsody-smoke.ps1
```

Rhapsody가 설치된 Windows PC에서는 실제 project 파일을 넘기면 COM 기반 read smoke까지 실행합니다.

```powershell
.\tests\rhapsody-smoke.ps1 -RhapsodyProjectPath "C:\path\model.rpyx"
```

모델 수정까지 검증하려면 명시적으로 write smoke를 켭니다. 이 명령은 smoke package/class를 만들고 project를 저장합니다.

```powershell
.\tests\rhapsody-smoke.ps1 -RhapsodyProjectPath "C:\path\model.rpyx" -RunWriteSmoke
```
