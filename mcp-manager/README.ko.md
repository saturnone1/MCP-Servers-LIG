# mcp-manager

Docker MCP 서버와 Windows-host MCP 서버를 한 곳에서 켜고 끄는 CLI 관리자입니다. 개발 중에는 Docker 컨테이너와 Windows 프로세스를 함께 제어할 수 있고, 통합 배포 번들에서는 Docker 기본 서버까지 모두 `.exe`로 실행하도록 제어할 수 있습니다.

## 실행

개발 상태에서:

```powershell
.\mcp-manager\scripts\run.ps1 status all
.\mcp-manager\scripts\run.ps1 start docker
.\mcp-manager\scripts\run.ps1 stop windows
```

매니저만 배포용 `.exe`로 생성:

```powershell
.\mcp-manager\scripts\publish-win.ps1
```

배포 폴더에는 다음 파일이 들어갑니다.

- `McpManager.exe`
- `mcp-manager.cmd`
- `start-all.cmd`
- `stop-all.cmd`
- `status.cmd`
- `servers.json`

전체 MCP 서버를 Windows `.exe` 번들로 묶어 배포하려면 저장소 루트에서 다음을 실행합니다.

```powershell
.\scripts\publish-mcp-bundle.ps1 -Zip
```

이 방식은 Docker가 기본이던 서버들도 모두 `win-x64` self-contained 실행 파일로 publish해서 `mcp-bundle` 아래에 넣습니다.

```text
mcp-bundle\McpManager.exe
mcp-bundle\servers.json
mcp-bundle\start-all.cmd
mcp-bundle\stop-all.cmd
mcp-bundle\status.cmd
mcp-bundle\mcp-filesystem-win-x64\McpFilesystem.exe
mcp-bundle\mcp-git-win-x64\McpGit.exe
mcp-bundle\mcp-matlab-win-x64\McpMatlab.exe
```

번들 안의 `servers.json`은 모두 `process` 방식으로 구성되어 있습니다. 즉 `McpManager.exe start all`은 Docker를 호출하지 않고 각 서버 폴더의 `Mcp*.exe`를 직접 실행합니다.

번들 환경변수는 서버별 폴더의 `<server>.env`에서 수정합니다. 예를 들어 `edit-env-mcp-jira.cmd`를 실행하거나 `mcp-bundle\mcp-jira-win-x64\mcp-jira.env`를 수정하고 `McpManager.exe restart mcp-jira`를 실행하면 됩니다. 공통 값은 번들 루트의 `common.env`, 서버별 루트 override는 `mcp-jira.env`처럼 둘 수 있고, 고급 설정은 `servers.json`의 `envFiles` 배열로 추가할 수 있습니다.

기존 번들에 새 manager만 덮어쓴 경우에는 `sync-env-files.ps1`을 한 번 실행해 `servers.json`의 env 값을 서버별 `.env` 파일로 분리합니다.

## 명령

인자 없이 `McpManager.exe`를 더블클릭하면 콘솔 메뉴가 열립니다. 메뉴에서 전체 시작/종료/재시작, 상태 확인, URL 확인, 서버별 시작/종료, 로그 확인을 선택할 수 있습니다.

```powershell
.\McpManager.exe list all
.\McpManager.exe start all
.\McpManager.exe stop all
.\McpManager.exe restart mcp-matlab
.\McpManager.exe status all
.\McpManager.exe health all
.\McpManager.exe logs mcp-filesystem
.\McpManager.exe urls all
```

## 대상 선택

- `all`
- `docker`
- `windows`
- 서버 이름: `mcp-matlab`, `mcp-filesystem`
- 그룹: `desktop`, `cad`, `database`, `devops`, `observability`, `office`

## 동작 방식

- 개발용 기본 설정(`config/servers.json`)에서 Docker 서버는 `docker run -d --name <server>`로 실행합니다.
- 개발용 기본 설정에서 Windows-host 서버는 `windows-host-publish/<server>-win-x64/Mcp*.exe`를 실행합니다.
- 통합 번들 설정(`config/servers.bundle.json`)에서는 모든 서버를 `mcp-bundle/<server>-win-x64/Mcp*.exe` 프로세스로 실행합니다.
- Windows process pid는 `.mcp-manager/*.pid`에 저장합니다.
- Windows process 로그는 `.mcp-manager/logs/*.log`에 저장합니다.
- 상태와 health는 `/healthz`로 확인합니다.

기존 전체 번들에 AutoCAD MCP만 교체해서 넣을 때는 저장소 루트에서 다음 스크립트로 `mcp-autocad-win-x64` 교체 패키지를 만듭니다.

```powershell
.\scripts\publish-autocad-bundle-patch.ps1 -Zip
```
