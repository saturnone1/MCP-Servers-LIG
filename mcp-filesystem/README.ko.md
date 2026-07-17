# mcp-filesystem

영어 버전: [README.md](README.md)

파일시스템 접근을 제공하는 C# MCP 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 참고 원본: `mark3labs/mcp-filesystem-server`
- 구현 방식: Go 코드를 직접 포팅하기보다 `System.IO`로 C# 재구현했습니다.
- 유지한 보안 모델: allowed root, 경로 정규화, symlink-aware canonical path, 쓰기 gate.
- trusted-local Docker 기본값: 로컬 테스트가 쉽도록 쓰기를 허용하고 `MCP_ALLOWED_DIRS=/`로 둡니다.

## 빌드

```powershell
docker build -t local/mcp-filesystem .
```

## Air Gap 추출

`local/mcp-filesystem:latest` 이미지를 `airgap/local-mcp-filesystem.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-filesystem -Port 8081
```

연결 주소:

- Streamable HTTP: `http://localhost:8081/mcp`
- Legacy SSE: `http://localhost:8081/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `list_allowed_directories` | 접근 허용된 컨테이너 root 경로를 나열합니다. |
| `read_file` | UTF-8 텍스트 파일을 읽습니다. |
| `read_multiple_files` | 여러 UTF-8 텍스트 파일을 한 번에 읽습니다. |
| `write_file` | UTF-8 텍스트 파일을 생성하거나 덮어씁니다. |
| `copy` | 파일 또는 디렉터리를 복사합니다. |
| `move` | 파일 또는 디렉터리를 이동합니다. |
| `delete` | 파일 또는 디렉터리를 삭제합니다. |
| `stat` | 파일 또는 디렉터리 메타데이터를 반환합니다. |
| `list_directory` | 패턴, 재귀, limit 옵션으로 디렉터리를 나열합니다. |
| `search` | 정규식으로 파일 이름을 검색합니다. |

## API 설명

경로 파라미터는 컨테이너 경로를 받습니다. `MCP_PATH_MAPPINGS`가 설정되어 있으면 Windows 호스트 경로도 그대로 받을 수 있습니다.

| Tool | Arguments | 반환 |
| --- | --- | --- |
| `list_allowed_directories` | 없음 | 허용 root string 배열 |
| `read_file` | `path` string, `maxBytes` int = `16777216` | 파일 텍스트(최대 64 MiB) |
| `read_multiple_files` | `paths` string array, `maxBytesPerFile` int = `16777216` | path별 텍스트 객체(파일당 최대 64 MiB) |
| `write_file` | `path` string, `content` string | 쓰기 결과 metadata |
| `copy` | `sourcePath` string, `destinationPath` string, `overwrite` bool = `false` | 복사 결과 metadata |
| `move` | `sourcePath` string, `destinationPath` string, `overwrite` bool = `false` | 이동 결과 metadata |
| `delete` | `path` string, `recursive` bool = `false` | 삭제 결과 metadata |
| `stat` | `path` string | 파일 또는 디렉터리 metadata |
| `list_directory` | `path` string = `.`, `pattern` string = `*`, `recursive` bool = `false`, `limit` int = `2000` | entry metadata 배열(최대 100,000개) |
| `search` | `path` string, `regex` string, `limit` int = `1000` | 검색 결과 metadata 배열(최대 100,000개) |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `MCP_ENABLE_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 write/copy/move/delete를 막습니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. Kubernetes 배포에서는 PVC를 `/workspace`에 마운트하고 `MCP_ALLOWED_DIRS=/workspace`, `MCP_ENABLE_WRITES=true`를 사용합니다. PVC 또는 다른 클러스터 네이티브 볼륨이 있으면 Linux Kubernetes에서 정상 동작합니다.
