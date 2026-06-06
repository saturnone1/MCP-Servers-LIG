# mcp-office

영어 버전: [README.md](README.md)

OfficeCLI를 MCP 원격 서버로 감싼 C# 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: `iOfficeAI/OfficeCLI`
- 구현 방식: Linux x64 OfficeCLI 릴리스를 Docker 이미지에 포함하고 MCP tool로 노출합니다.
- 추가 호환성: legacy `.doc` 텍스트 추출을 위해 `antiword`를 함께 설치합니다.
- 런타임 목표: headless, Office-free. 컨테이너 안에 Microsoft Office 설치가 필요 없습니다.

## 빌드

```powershell
docker build -t local/mcp-office .
```

Dockerfile은 빌드 시점에 OfficeCLI Linux x64 릴리스를 다운로드해 최종 이미지에 포함합니다. 따라서 런타임에는 인터넷이 필요 없습니다.

## 실행

```powershell
docker run --rm -p 8080:8080 -v ${PWD}:/workspace local/mcp-office
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
| `render_document` | 문서를 지정한 출력 경로로 렌더링하거나 내보냅니다. |
| `run_office_cli` | 고급 작업을 위해 raw OfficeCLI 인자를 실행합니다. |

## API 설명

모든 tool은 명령 실행 형태의 객체를 반환합니다: `{ "exitCode": number, "stdout": string, "stderr": string }`.

| Tool | Arguments | 설명 |
| --- | --- | --- |
| `version` | 없음 | 이미지에 포함된 OfficeCLI 버전을 반환합니다. |
| `inspect_document` | `path` string, `mode` string = `text` | OfficeCLI로 `.docx`, `.xlsx`, `.pptx` 등 지원 문서를 검사합니다. |
| `extract_text` | `path` string, `maxLines` int = `200` | 최신 Office 파일은 OfficeCLI, legacy `.doc`는 `antiword`로 텍스트를 추출합니다. |
| `create_document` | `path` string | 매핑된 경로에 문서를 생성합니다. |
| `apply_batch` | `documentPath` string, `batchJsonPath` string | OfficeCLI batch JSON을 문서에 적용합니다. |
| `render_document` | `documentPath` string, `outputPath` string | 문서를 지정한 출력 경로로 렌더링하거나 내보냅니다. |
| `run_office_cli` | `args` string array, `timeoutMs` int = `120000` | raw OfficeCLI 인자를 직접 실행하는 고급용 tool입니다. |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `MCP_ENABLE_OFFICE_WRITES` | Dockerfile에서 `true` | `false`로 설정하면 문서 생성/편집/렌더 같은 변경 작업을 막습니다. |
| `OFFICECLI_PATH` | 이미지에 포함된 OfficeCLI 경로 | OfficeCLI 실행 파일 경로를 바꿉니다. |
| `ANTIWORD_PATH` | `antiword` | legacy `.doc` 추출기 경로를 바꿉니다. |

## 참고

`extract_text`는 최신 Office 파일은 OfficeCLI로 읽고, legacy `.doc` 파일은 `antiword`로 읽습니다.
