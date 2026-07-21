# LIG AI MCP 설치 파일

`scripts\build-installer.ps1`은 Windows x64 MCP 번들을 게시하고 MSI 및 UAC 상승용 단일 `Setup.exe`로 압축합니다. Setup은 시작할 때 바로 관리자 권한을 요청하고 MCP-PDF용 Poppler 선택 설치 여부를 물은 뒤 내장 MSI를 Windows Installer로 실행하므로 비승격 MSI 실행으로 인한 2502/2503 오류를 방지합니다.

```powershell
.\scripts\build-installer.ps1
```

버전은 `installer\VERSION`에서 읽습니다. 정식 배포 시에는 다음처럼 코드 서명 인증서 지문을 지정합니다.

```powershell
.\scripts\build-installer.ps1 -CertificateThumbprint '<인증서 지문>'
```

사용자에게 제공되는 결과 파일은 `installer\output\LIG-AI-MCP-Setup-<version>-win-x64.exe` 하나뿐입니다. MSI는 Setup에 내장되는 중간 빌드 파일이며 사용자 배포 폴더에는 생성하지 않습니다. 빌드 도구 WiX 5.0.2는 저장소의 무시된 `.tools\wix`에 설치됩니다.

빌드에는 `Library\bin\pdftoppm.exe`를 포함하는 portable Poppler 배포 루트가 필요합니다. `oschwartz10612.Poppler`의 winget 설치를 자동 탐색하며, 다른 검증된 배포본은 다음처럼 지정할 수 있습니다.

```powershell
.\scripts\build-installer.ps1 -PopplerRoot 'D:\dependencies\poppler-25.07.0'
```

또는 `LIG_POPPLER_ROOT` 환경 변수를 사용할 수 있습니다. 빌드는 Poppler 전체 폴더를 `mcp-bundle\dependencies\poppler`에 스테이징하고 `pdftoppm -v`와 SHA-256을 확인합니다. 정식 재배포 전에는 사용한 Windows 배포본의 전체 라이선스·제3자 고지와 소스 제공 의무를 검토해야 합니다.

## 배포

생성된 `LIG-AI-MCP-Setup-<version>-win-x64.exe` 파일 하나만 배포합니다. Setup 자체가 설치 시작 전에 UAC 관리자 권한을 요청하며, Poppler 설치 여부를 기본값 `예`인 `예/아니요/취소` 질문으로 표시합니다. 이후 내장 MSI를 관리자와 SYSTEM이 접근할 수 있는 전용 작업 폴더에 풀고 기본 진행 창과 완료 창을 표시합니다. 앱 목록과 시작 메뉴의 제거는 관리자 권한 전용 Uninstaller가 담당하며 제거 진행 창과 완료 창을 직접 표시합니다. `mcp-bundle` 폴더, MSI, ZIP, Poppler ZIP, WiX 도구 또는 별도의 .NET/ASP.NET Core 런타임 설치 파일은 필요하지 않습니다.

MSI에는 MCP 매니저, MCP 서버 20개, OfficeCLI, 공유 .NET 10/ASP.NET Core 10 런타임과 선택 설치형 portable Poppler가 포함됩니다. Poppler를 선택하면 `C:\Program Files\LIG AI MCP\dependencies\poppler`에 설치되고 MCP-PDF가 내장 `pdftoppm.exe`를 자동 발견합니다. 선택하지 않으면 기존 `PDF_RENDER_COMMAND` 또는 PATH의 `pdftoppm`을 사용할 수 있으며, 어느 것도 없으면 페이지 렌더링만 사용할 수 없습니다. MCP-PDF의 변환 기능에 필요한 Docling Serve 또는 로컬 Docling CLI는 여전히 별도로 준비해야 합니다.

- Git MCP: `git.exe`
- Docker MCP: Docker CLI와 Docker Desktop/daemon
- Kubernetes MCP: `kubectl.exe`와 kubeconfig
- .NET MCP의 빌드/테스트 기능: .NET SDK
- MATLAB, AutoCAD, SolidWorks, Rhapsody MCP: 해당 프로그램, 라이선스 및 COM/CLI 환경
- DB 및 원격 API MCP: 연결 문자열, URL, 계정 또는 토큰
- HWP 고급 변환: 필요에 따라 `hwp5txt` 또는 LibreOffice

자동 배포 옵션:

```text
--quiet --with-poppler       조용한 설치, Poppler 포함
--quiet --without-poppler    조용한 설치, Poppler 제외
```

`--quiet`만 사용하면 Poppler를 포함합니다. 대화형 설치에서는 명령행 옵션을 지정하지 않았을 때만 질문을 표시합니다.

인증서 지문을 지정하면 제품 실행 파일, MSI 및 최종 Setup을 SHA-256과 타임스탬프로 서명합니다. 인증서를 지정하지 않으면 빌드는 계속되지만 서명되지 않았다는 경고를 표시합니다.

설치 프로그램은 다음을 제공합니다.

- UAC 상승 후 컴퓨터 전체에 설치
- 앱 목록에서 관리자 권한 Uninstaller를 통한 제거만 제공하고, 실패한 업그레이드는 이전 설치로 롤백
- 번들 공유 .NET 및 ASP.NET Core 런타임 포함
- MCP-PDF 페이지 렌더링용 portable Poppler 선택 설치와 자동 경로 탐지
- 실행할 때마다 UAC 관리자 권한을 요청하며 작업표시줄에 제품 아이콘이 표시되는 self-contained `McpManager.exe`
- 시작 메뉴와 바탕화면의 `LIG AI MCP` 바로가기
- Windows 앱 목록을 통한 업그레이드 및 제거
- 제거 시 모든 로컬 사용자 프로필의 로그, PID 상태 및 자동실행 설정 정리

이미 게시한 번들을 다시 압축만 하려면 다음 명령을 사용합니다.

```powershell
.\scripts\build-installer.ps1 -SkipBundle
```

릴리스 설치본에는 PDB와 `*.old` 백업 파일을 포함하지 않습니다. `autostart.json`도 설치 파일에 넣지 않으며 각 사용자가 설치 후 등록한 자동실행 설정만 사용합니다.

업그레이드 설치본을 만들 때는 이전 설치본보다 높은 `-Version` 값을 사용해야 합니다. 내장 MSI가 고정된 `UpgradeCode`를 유지하므로 새 Setup을 실행하면 기존 버전을 감지해 업그레이드합니다.
