# mcp-office

영어 버전: [README.md](README.md)

OfficeCLI를 MCP 원격 서버로 감싼 C# 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: `iOfficeAI/OfficeCLI`
- 구현 방식: Linux x64 OfficeCLI 릴리스를 Docker 이미지에 포함하고 MCP tool로 노출합니다.
- Windows exe 번들: Windows x64 OfficeCLI 릴리스를 `tools/officecli.exe`로 함께 포함합니다.
- 추가 호환성: legacy `.doc` 텍스트 추출은 `antiword`가 있으면 우선 사용하고, 없으면 OfficeCLI로 fallback합니다.
- 런타임 목표: headless, Office-free. 컨테이너 안에 Microsoft Office 설치가 필요 없습니다.

## 빌드

```powershell
docker build -t local/mcp-office .
```

Dockerfile은 빌드 시점에 OfficeCLI Linux x64 릴리스를 다운로드해 최종 이미지에 포함합니다. 따라서 런타임에는 인터넷이 필요 없습니다. air gap 환경에서는 인터넷 되는 PC에서 이미지를 build/export한 뒤 `docker load`로 가져갑니다.

Windows exe 번들은 `mcp-office\scripts\download-officecli.ps1`로 받은 OfficeCLI Windows x64 binary를 publish 시점에 함께 복사합니다.

## Air Gap 추출

`local/mcp-office:latest` 이미지를 `airgap/local-mcp-office.tar`로 추출하고 air gap PC에서 `docker load` 후 실행하는 방법은 [airgap/README.ko.md](airgap/README.ko.md)에 정리되어 있습니다.

## 실행

```powershell
.\scripts\run-docker-mcp.ps1 -Server mcp-office -Port 8080
```

연결 주소:

- Streamable HTTP: `http://localhost:8080/mcp`
- Legacy SSE: `http://localhost:8080/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `version` | `officecli --version`을 반환합니다. |
| `inspect_document` | OfficeCLI inspection/dump 모드로 문서를 검사합니다. |
| `extract_text` | `.doc`, `.docx`, `.xlsx`, `.pptx`에서 읽을 수 있는 텍스트를 추출합니다. |
| `create_document` | OfficeCLI로 Office 문서를 생성합니다. |
| `apply_batch` | OfficeCLI batch JSON을 문서에 적용합니다. |
| `render_document` | OfficeCLI 텍스트 view 결과를 지정한 출력 경로로 저장합니다. |
| `run_office_cli` | 고급 작업을 위해 raw OfficeCLI 인자를 실행합니다. |

## API 설명

모든 tool은 명령 실행 형태의 객체를 반환합니다: `{ "exitCode": number, "stdout": string, "stderr": string }`.

| Tool | Arguments | 설명 |
| --- | --- | --- |
| `version` | 없음 | 이미지에 포함된 OfficeCLI 버전을 반환합니다. |
| `inspect_document` | `path` string, `mode` string = `text` | OfficeCLI로 `.docx`, `.xlsx`, `.pptx` 등 지원 문서를 검사합니다. |
| `extract_text` | `path` string, `maxLines` int = `2000` | 최대 100,000줄, 출력 64 MiB까지 추출합니다. |
| `create_document` | `path` string | 매핑된 경로에 문서를 생성합니다. |
| `apply_batch` | `documentPath` string, `batchJsonPath` string | OfficeCLI batch JSON을 문서에 적용합니다. |
| `render_document` | `documentPath` string, `outputPath` string | OfficeCLI `view text --json` 결과를 지정 경로에 저장합니다. PDF/HTML/이미지 렌더링이 아니라 텍스트 스냅샷입니다. |
| `run_office_cli` | `args` string array, `timeoutMs` int = `600000` | 최대 24시간, 출력 64 MiB의 raw OfficeCLI tool입니다. |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `MCP_ENABLE_OFFICE_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 문서 생성/편집/렌더 같은 변경 작업을 막습니다. |
| `OFFICECLI_PATH` | 이미지에 포함된 OfficeCLI 경로 | OfficeCLI 실행 파일 경로를 바꿉니다. |
| `ANTIWORD_PATH` | `antiword` | 선택적 legacy `.doc` 추출기 경로를 바꿉니다. 없으면 OfficeCLI로 fallback합니다. |

## 참고

`extract_text`는 최신 Office 파일은 OfficeCLI로 읽고, legacy `.doc` 파일은 `antiword`가 있으면 `antiword`를 우선 사용합니다. `antiword`가 없으면 OfficeCLI로 fallback합니다.

## Kubernetes

이번 단계에서는 `mcp-office`용 Kubernetes 매니페스트를 제공하지 않습니다. 문서 변환/데스크톱 파일 처리 성격이 강하고, 이번 검토에서 로컬/Windows 지향 서버로 분류했기 때문입니다.
