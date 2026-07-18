# LIG AI MCP 설치 파일

`scripts\build-installer.ps1`은 Windows x64 MCP 번들을 게시하고 MSI 및 UAC 상승용 단일 `Setup.exe`로 압축합니다. Setup은 시작할 때 바로 관리자 권한을 요청한 뒤 내장 MSI를 Windows Installer로 실행하므로 비승격 MSI 실행으로 인한 2502/2503 오류를 방지합니다.

```powershell
.\scripts\build-installer.ps1 -Version 1.0.0
```

사용자에게 제공되는 결과 파일은 `installer\output\LIG-AI-MCP-Setup-<version>-win-x64.exe` 하나뿐입니다. MSI는 Setup에 내장되는 중간 빌드 파일이며 사용자 배포 폴더에는 생성하지 않습니다. 빌드 도구 WiX 5.0.2는 저장소의 무시된 `.tools\wix`에 설치됩니다.

## 배포

생성된 `LIG-AI-MCP-Setup-<version>-win-x64.exe` 파일 하나만 배포합니다. Setup 자체가 설치 시작 전에 UAC 관리자 권한을 요청하며, 내장 MSI를 관리자와 SYSTEM이 접근할 수 있는 전용 작업 폴더에 풀고 기본 진행 창과 완료 창을 표시합니다. 앱 목록과 시작 메뉴의 제거는 관리자 권한 전용 Uninstaller가 담당하며 제거 진행 창과 완료 창을 직접 표시합니다. `mcp-bundle` 폴더, MSI, ZIP, WiX 도구 또는 별도의 .NET/ASP.NET Core 런타임 설치 파일은 필요하지 않습니다.

MSI에는 MCP 매니저, MCP 서버 19개, OfficeCLI 및 공유 .NET 10/ASP.NET Core 10 런타임이 포함됩니다. 다만 다음 프로그램과 접속 정보는 사용하는 MCP 기능에 따라 대상 PC에 별도로 준비해야 합니다.

- Git MCP: `git.exe`
- Docker MCP: Docker CLI와 Docker Desktop/daemon
- Kubernetes MCP: `kubectl.exe`와 kubeconfig
- .NET MCP의 빌드/테스트 기능: .NET SDK
- MATLAB, AutoCAD, SolidWorks, Rhapsody MCP: 해당 프로그램, 라이선스 및 COM/CLI 환경
- DB 및 원격 API MCP: 연결 문자열, URL, 계정 또는 토큰
- HWP 고급 변환: 필요에 따라 `hwp5txt` 또는 LibreOffice

현재 Setup과 MSI는 코드 서명되지 않았습니다. 조직 외부 또는 보안 정책이 강한 환경에 배포하면 Windows가 알 수 없는 게시자 경고를 표시할 수 있으므로 정식 배포 전 조직의 코드 서명 인증서로 서명하는 것을 권장합니다.

설치 프로그램은 다음을 제공합니다.

- UAC 상승 후 컴퓨터 전체에 설치
- 앱 목록에서 관리자 권한 Uninstaller를 통한 제거만 제공하고, 실패한 업그레이드는 이전 설치로 롤백
- 번들 공유 .NET 및 ASP.NET Core 런타임 포함
- 실행할 때마다 UAC 관리자 권한을 요청하며 작업표시줄에 제품 아이콘이 표시되는 self-contained `McpManager.exe`
- 시작 메뉴와 바탕화면의 `LIG AI MCP` 바로가기
- Windows 앱 목록을 통한 업그레이드 및 제거
- 제거 시 로그, PID 상태 및 자동실행 설정 정리

이미 게시한 번들을 다시 압축만 하려면 다음 명령을 사용합니다.

```powershell
.\scripts\build-installer.ps1 -Version 1.0.0 -SkipBundle
```

릴리스 설치본에는 PDB와 `*.old` 백업 파일을 포함하지 않습니다. `autostart.json`도 설치 파일에 넣지 않으며 각 사용자가 설치 후 등록한 자동실행 설정만 사용합니다.

업그레이드 설치본을 만들 때는 이전 설치본보다 높은 `-Version` 값을 사용해야 합니다. 내장 MSI가 고정된 `UpgradeCode`를 유지하므로 새 Setup을 실행하면 기존 버전을 감지해 업그레이드합니다.
