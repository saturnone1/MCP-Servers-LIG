# LIG AI MCP 설치 파일

`scripts\build-installer.ps1`은 Windows x64 MCP 번들을 게시하고 MSI를 UAC 상승용 단일 `Setup.exe`에 내장합니다. Setup은 시작 즉시 관리자 권한을 요청하고 Windows Installer의 진행 창과 완료 결과를 표시하므로 비승격 실행에서 발생하는 2502·2503 오류를 방지합니다.

```powershell
.\scripts\build-installer.ps1
```

버전은 `installer\VERSION`에서 읽습니다. 정식 배포에서 코드 서명이 필요하면 인증서 지문을 지정합니다.

```powershell
.\scripts\build-installer.ps1 -CertificateThumbprint '<인증서 지문>'
```

## 배포 결과

사용자에게는 `installer\output\LIG-AI-MCP-Setup-<version>-win-x64.exe` 파일 하나만 제공합니다. MSI, WiX 도구, `mcp-bundle` 폴더와 별도 .NET·ASP.NET Core 런타임 설치 파일은 배포하지 않아도 됩니다.

설치 파일에는 다음 항목이 포함됩니다.

- MCP 서버 19개
- 공유 .NET 10 및 ASP.NET Core 10 런타임
- self-contained `McpManager.exe`
- 시작 메뉴 및 바탕화면 바로가기
- 관리자 권한 전용 Uninstaller
- 실패 시 이전 설치로 복구되는 업그레이드 구성

`McpManager.exe`는 실행할 때마다 UAC 관리자 권한을 요청하며 여기서 시작한 서버는 해당 권한을 상속합니다. 개별 MCP 서버 실행 파일은 MCP 클라이언트가 직접 실행할 수 있도록 자체 승격을 강제하지 않습니다.

일부 MCP 기능은 대상 PC에 설치된 외부 프로그램이나 접속 정보를 사용합니다.

- Git MCP: `git.exe`
- Docker MCP: Docker CLI 및 Docker Desktop/daemon
- Kubernetes MCP: `kubectl.exe`와 kubeconfig
- .NET MCP 빌드·테스트: 해당 .NET SDK
- MATLAB, AutoCAD, SolidWorks, Rhapsody MCP: 프로그램, 라이선스 및 COM/CLI 환경
- DB 및 원격 API MCP: 연결 문자열, URL, 계정 또는 토큰
- HWP 고급 변환: 필요에 따라 `hwp5txt` 또는 LibreOffice

## 자동화 옵션

조용한 설치는 다음처럼 실행합니다.

```text
--quiet
```

인증서를 지정하면 제품 실행 파일, MSI와 최종 Setup을 SHA-256 및 타임스탬프로 서명합니다. 인증서를 지정하지 않으면 빌드는 계속되지만 서명되지 않았다는 경고를 표시합니다.

이미 게시한 번들을 다시 포장만 하려면 다음 명령을 사용합니다.

```powershell
.\scripts\build-installer.ps1 -SkipBundle
```

릴리스 설치본에는 PDB와 `*.old` 백업 파일을 포함하지 않습니다. `autostart.json`도 설치 파일에 넣지 않아 새 사용자가 설치 전 자동실행 설정을 물려받지 않습니다. 업그레이드 설치본은 기존 설치보다 높은 `installer\VERSION` 값을 사용해야 합니다.
