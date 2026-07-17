# mcp-docker

영어 버전: [README.md](README.md)

Docker 작업을 MCP tool로 제공하는 C# 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: Docker CLI를 감싸는 C# MCP 서버입니다.
- 런타임 요구사항: Docker socket을 컨테이너에 마운트해야 합니다.
- trusted-local Docker 기본값: 컨테이너/이미지 변경 작업을 허용합니다.

## 빌드

```powershell
docker build -t local/mcp-docker .
```

## Air Gap 추출

`local/mcp-docker:latest` 이미지를 `airgap/local-mcp-docker.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
docker run --rm -p 127.0.0.1:8088:8080 `
  -v /var/run/docker.sock:/var/run/docker.sock `
  local/mcp-docker
```

연결 주소:

- HTTP: `http://localhost:8088/mcp`
- SSE: `http://localhost:8088/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `version` | Docker client/server version을 반환합니다. |
| `list_containers` | container 목록을 반환합니다. |
| `list_images` | image 목록을 반환합니다. |
| `inspect` | container 또는 image를 inspect합니다. |
| `logs` | container log를 조회합니다. |
| `run_container` | container를 실행합니다. |
| `start_container` | container를 시작합니다. |
| `stop_container` | container를 중지합니다. |
| `remove_container` | container를 삭제합니다. |
| `pull_image` | image를 pull합니다. |
| `remove_image` | image를 삭제합니다. |

## API 설명

모든 tool은 `{ "exitCode": number, "stdout": string, "stderr": string }` 형태를 반환합니다.

| Tool | Arguments |
| --- | --- |
| `version` | 없음 |
| `list_containers` | `all` bool = `true`, `format` string = `json` |
| `list_images` | `format` string = `json` |
| `inspect` | `target` string |
| `logs` | `container` string, `tail` int = `200`, `timestamps` bool = `false` |
| `run_container` | `image` string, `name` string? = `null`, `args` string array? = `null`, `detach` bool = `true`, `ports` string array? = `null`, `volumes` string array? = `null`, `environment` string array? = `null` |
| `start_container` | `container` string |
| `stop_container` | `container` string, `timeoutSeconds` int = `10` |
| `remove_container` | `container` string, `force` bool = `false` |
| `pull_image` | `image` string |
| `remove_image` | `image` string, `force` bool = `false` |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ENABLE_DOCKER_WRITES` | `true` | `false`로 설정하면 container/image 시작·중지·생성·삭제·pull을 차단합니다. |
| `DOCKER_PATH` | `docker` | Docker CLI 실행 파일 경로입니다. |

## Kubernetes

이번 단계에서는 `mcp-docker`용 Kubernetes 매니페스트를 제공하지 않습니다. 이 서버는 `/var/run/docker.sock`을 통한 Docker daemon 접근이 필요한데, 많은 Kubernetes 클러스터는 Docker socket이 없는 containerd 기반이고, host socket을 마운트하면 노드의 컨테이너 제어 권한을 Pod에 주는 고권한 구성이 됩니다.
