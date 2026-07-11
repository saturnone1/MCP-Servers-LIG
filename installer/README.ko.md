# LIG AI MCP 설치 파일

`scripts\build-installer.ps1`은 Windows x64 MCP 번들을 게시하고 단일 MSI 설치 파일로 압축합니다.

```powershell
.\scripts\build-installer.ps1 -Version 1.0.0
```

결과 파일은 `installer\output\LIG-AI-MCP-Setup-<version>-win-x64.msi`입니다. 빌드 도구 WiX 5.0.2는 최초 실행 시 저장소의 무시된 `.tools\wix` 폴더에만 설치됩니다.

## 배포

사용자에게는 생성된 `LIG-AI-MCP-Setup-<version>-win-x64.msi` 파일 하나만 배포하면 됩니다. `mcp-bundle` 폴더, ZIP, WiX 도구 또는 별도의 .NET/ASP.NET Core 런타임 설치 파일을 함께 전달할 필요가 없습니다.

MSI에는 MCP 매니저, MCP 서버 19개, OfficeCLI 및 공유 .NET 10/ASP.NET Core 10 런타임이 포함됩니다. 다만 다음 프로그램과 접속 정보는 사용하는 MCP 기능에 따라 대상 PC에 별도로 준비해야 합니다.

- Git MCP: `git.exe`
- Docker MCP: Docker CLI와 Docker Desktop/daemon
- Kubernetes MCP: `kubectl.exe`와 kubeconfig
- .NET MCP의 빌드/테스트 기능: .NET SDK
- MATLAB, AutoCAD, SolidWorks, Rhapsody MCP: 해당 프로그램, 라이선스 및 COM/CLI 환경
- DB 및 원격 API MCP: 연결 문자열, URL, 계정 또는 토큰
- HWP 고급 변환: 필요에 따라 `hwp5txt` 또는 LibreOffice

현재 MSI는 코드 서명되지 않았습니다. 조직 외부 또는 보안 정책이 강한 환경에 배포하면 Windows가 알 수 없는 게시자 경고를 표시할 수 있으므로 정식 배포 전 조직의 코드 서명 인증서로 서명하는 것을 권장합니다.

설치 프로그램은 다음을 제공합니다.

- 현재 사용자용 설치로 관리자 권한 요구 최소화
- 번들 공유 .NET 및 ASP.NET Core 런타임 포함
- 시작 메뉴와 바탕화면의 `LIG AI MCP` 바로가기
- Windows 앱 목록을 통한 업그레이드 및 제거
- 제거 시 로그, PID 상태 및 자동실행 설정 정리

이미 게시한 번들을 다시 압축만 하려면 다음 명령을 사용합니다.

```powershell
.\scripts\build-installer.ps1 -Version 1.0.0 -SkipBundle
```

릴리스 설치본에는 PDB와 `*.old` 백업 파일을 포함하지 않습니다. `autostart.json`도 설치 파일에 넣지 않으며 각 사용자가 설치 후 등록한 자동실행 설정만 사용합니다.

업그레이드 설치본을 만들 때는 이전 MSI보다 높은 `-Version` 값을 사용해야 합니다. 같은 `UpgradeCode`를 유지하므로 새 MSI를 실행하면 기존 버전을 감지해 업그레이드합니다.
