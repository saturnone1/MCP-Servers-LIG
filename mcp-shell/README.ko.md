# mcp-shell

영어 버전: [README.md](README.md)

컨테이너 내부 명령 실행을 제공하는 C# MCP 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: C# `ProcessStartInfo`로 직접 구현했습니다.
- 목적: 신뢰할 수 있는 로컬 환경에서 컨테이너 내부 명령을 MCP로 실행합니다.
- 안전 장치: 실행 여부, 허용 command, 허용 env var를 환경 변수로 제한할 수 있습니다. Docker 기본값은 로컬 테스트를 위해 실행 허용입니다.

## 빌드

```powershell
docker build -t local/mcp-shell .
```

## Air Gap 추출

`local/mcp-shell:latest` 이미지를 `airgap/local-mcp-shell.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-shell -Port 8083
```

연결 주소:

- Streamable HTTP: `http://localhost:8083/mcp`
- Legacy SSE: `http://localhost:8083/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `run_command` | command, args, working directory, timeout, max output size, optional environment를 받아 명령을 실행합니다. |

## API 설명

| Tool | Arguments | 반환 |
| --- | --- | --- |
| `run_command` | `command` string, `args` string array = `[]`, `workingDirectory` string = `/workspace`, `timeoutMs` int = `300000`, `maxOutputBytes` int = `16777216`, `environment` object? = `null` | 최대 24시간, 출력 64 MiB. env 허용 목록이 비어 있으면 전달한 환경변수를 모두 허용합니다. |

`workingDirectory`는 매핑된 Windows 호스트 경로도 받을 수 있습니다. `MCP_SHELL_ALLOWED_ENV`가 설정되어 있으면 `environment`는 해당 allowlist로 필터링됩니다.

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ENABLE_SHELL` | Dockerfile에서 `true` | `false`로 설정하면 shell 실행을 막습니다. |
| `MCP_ALLOWED_DIRS` | `/` | working directory로 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `MCP_SHELL_ALLOWED_COMMANDS` | 빈 값 | 선택적 command allowlist입니다. 빈 값이면 모든 command를 허용합니다. |
| `MCP_SHELL_ALLOWED_ENV` | 빈 값 | 선택적 환경 변수 allowlist이며 빈 값이면 전달한 변수를 모두 허용합니다. |

## Kubernetes

이번 단계에서는 `mcp-shell`용 Kubernetes 매니페스트를 제공하지 않습니다. 임의 명령 실행을 의도적으로 노출하는 서버라서 기본 클러스터 서비스보다는 신뢰된 로컬/디버그 컨테이너로 다루는 편이 맞습니다.
