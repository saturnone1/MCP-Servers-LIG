# LIG AI MCP 설치 파일

`scripts\build-installer.ps1`은 Windows x64 MCP 번들을 게시하고 단일 MSI 설치 파일로 압축합니다.

```powershell
.\scripts\build-installer.ps1 -Version 1.0.0
```

결과 파일은 `installer\output\LIG-AI-MCP-Setup-<version>-win-x64.msi`입니다. 빌드 도구 WiX 5.0.2는 최초 실행 시 저장소의 무시된 `.tools\wix` 폴더에만 설치됩니다.

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
