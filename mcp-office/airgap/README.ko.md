# mcp-office Air Gap 사용법

이 폴더는 `local/mcp-office:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-office
```

생성 파일:

```text
mcp-office\airgap\local-mcp-office.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-office\airgap\local-mcp-office.tar
```

## 실행

```powershell
.\mcp-office\airgap\run-docker-mcp.ps1 -Server mcp-office -Port 8080
```

연결 주소:

- HTTP: `http://localhost:8080/mcp`
- SSE: `http://localhost:8080/sse`

## Air Gap 참고

OfficeCLI, `antiword`, .NET 런타임은 이미지 안에 포함됩니다. 런타임 인터넷은 필요 없습니다. 실행 helper가 준비된 Windows 드라이브를 모두 자동 마운트합니다.
