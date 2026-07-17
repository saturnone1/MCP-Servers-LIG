# mcp-hwp

영어 버전: [README.md](README.md)

한글 HWP/HWPX 파일을 다루는 C# MCP 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: HWP/HWPX 처리를 위해 오픈 도구를 조합한 신규 C# MCP 서버입니다.
- `.hwp`: 내장 C# OLE/BodyText 파서로 먼저 텍스트를 추출하고, 실패하면 선택적 `hwp5txt`, LibreOffice headless 변환 순서로 fallback합니다.
- `.hwpx`: ZIP/XML 파일로 직접 열어 document XML의 text node를 읽습니다.
- 변환: `txt` 출력은 텍스트 추출 결과로 직접 저장합니다. `docx`, `pdf`, `odt` 출력은 LibreOffice를 사용하고, LibreOffice가 실제 출력 파일을 만들지 못하면 오류로 처리합니다.

## 빌드

```powershell
docker build -t local/mcp-hwp .
```

## Air Gap 추출

`local/mcp-hwp:latest` 이미지를 `airgap/local-mcp-hwp.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-hwp -Port 8086
```

연결 주소:

- Streamable HTTP: `http://localhost:8086/mcp`
- Legacy SSE: `http://localhost:8086/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `extract_text` | `.hwp`, `.hwpx`에서 읽을 수 있는 텍스트를 추출합니다. |
| `inspect` | 기본 파일 메타데이터를 반환합니다. |
| `convert` | `.hwp` 또는 `.hwpx`를 `txt`, `docx`, `pdf`, `odt`로 변환합니다. `txt`는 텍스트 추출기로 생성하고, 나머지 형식은 LibreOffice를 사용합니다. |

## API 설명

| Tool | Arguments | 반환 |
| --- | --- | --- |
| `extract_text` | `path` string, `maxChars` int = `1000000` | 최대 10,000,000자의 추출 텍스트입니다. |
| `inspect` | `path` string | metadata 객체. 파일이 없으면 `{ "exists": false, "requestedPath": ..., "mappedPath": ..., "error": ... }`를 반환합니다. |
| `convert` | `path` string, `outputDirectory` string = `/tmp/hwp-output`, `format` string = `txt`, `timeoutMs` int = `600000` | 최대 24시간, 출력 64 MiB입니다. `txt`는 추출 텍스트를 저장하고 나머지는 LibreOffice를 사용합니다. |

지원 format은 `txt`, `docx`, `pdf`, `odt`입니다.

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `MCP_ENABLE_HWP_WRITES` | `true` | `false`로 설정하면 `convert`를 차단합니다. |
| `HWP5TXT_PATH` | `hwp5txt` | 선택적 fallback `hwp5txt` 실행 파일 경로를 바꿉니다. |
| `SOFFICE_PATH` | `soffice` | 선택적 LibreOffice 실행 파일 경로를 바꿉니다. |

## 참고

`.hwp`는 내장 C# 파서를 우선 사용합니다. 내장 파서가 텍스트를 찾지 못하면 `hwp5txt`, LibreOffice 순서로 fallback합니다. `.hwpx`는 ZIP/XML로 직접 파싱합니다. `convert`의 `txt` 출력은 이 텍스트 추출 결과를 파일로 저장합니다. `docx`, `pdf`, `odt` 변환은 LibreOffice가 실제 출력 파일을 만들지 못하면 오류로 처리합니다.

## Kubernetes

이번 단계에서는 `mcp-hwp`용 Kubernetes 매니페스트를 제공하지 않습니다. 문서 변환 도구, font, 호스트 문서 접근 방식이 얽혀 있어 별도의 클러스터 스토리지/렌더링 검토가 필요하기 때문입니다.
