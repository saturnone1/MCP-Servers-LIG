# mcp-hwp

영어 버전: [README.md](README.md)

한글 HWP/HWPX 파일을 다루는 C# MCP 원격 서버입니다. Streamable HTTP와 legacy SSE를 모두 지원합니다.

## 원본 / 구현 방식

- 포팅 원본: 없음
- 구현 방식: HWP/HWPX 처리를 위해 오픈 도구를 조합한 신규 C# MCP 서버입니다.
- `.hwp`: `pyhwp`/`hwp5txt`로 먼저 텍스트를 추출하고, 실패하면 LibreOffice headless 변환으로 fallback합니다.
- `.hwpx`: ZIP/XML 파일로 직접 열어 document XML의 text node를 읽습니다.
- 변환: LibreOffice를 사용해 `txt`, `docx`, `pdf`, `odt`로 변환합니다.

## 빌드

```powershell
docker build -t local/mcp-hwp .
```

## 실행

```powershell
docker run --rm -p 8086:8080 -v ${PWD}:/workspace local/mcp-hwp
```

연결 주소:

- Streamable HTTP: `http://localhost:8086/mcp`
- Legacy SSE: `http://localhost:8086/sse`

## 도구

| Tool | 기능 |
| --- | --- |
| `extract_text` | `.hwp`, `.hwpx`에서 읽을 수 있는 텍스트를 추출합니다. |
| `inspect` | 기본 파일 메타데이터를 반환합니다. |
| `convert` | `.hwp` 또는 `.hwpx`를 `txt`, `docx`, `pdf`, `odt`로 변환합니다. |

## 환경 변수

| 변수 | 기본값 | 설명 |
| --- | --- | --- |
| `MCP_ALLOWED_DIRS` | `/` | 접근 가능한 컨테이너 root 경로입니다. |
| `MCP_PATH_MAPPINGS` | 빈 값 | Windows 호스트 경로를 Linux 컨테이너 경로로 매핑합니다. |
| `HWP5TXT_PATH` | `/opt/pyhwp/bin/hwp5txt` | `hwp5txt` 실행 파일 경로를 바꿉니다. |
| `SOFFICE_PATH` | `/usr/bin/soffice` | LibreOffice 실행 파일 경로를 바꿉니다. |

## 참고

`.hwp`는 `pyhwp`/`hwp5txt`를 우선 사용하고 LibreOffice를 fallback으로 사용합니다. `.hwpx`는 ZIP/XML로 직접 파싱합니다.
