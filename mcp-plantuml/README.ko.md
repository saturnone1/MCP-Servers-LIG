# mcp-plantuml

English version: [README.md](README.md)

PlantUML 다이어그램을 Streamable HTTP와 레거시 SSE로 렌더링하는 C# 원격 MCP 서버입니다.

## 계보

- 참고한 도구 표면: [infobip/plantuml-mcp-server](https://github.com/infobip/plantuml-mcp-server)(TypeScript, MIT) 등 커뮤니티 PlantUML MCP 서버.
- 직접 구현한 이유: 기존 서버들은 다이어그램을 인코딩해 `https://www.plantuml.com/plantuml`에 렌더링을 맡깁니다. 이 서버는 `plantuml.jar` 또는 `plantuml` CLI로 **로컬 렌더링**하므로 다이어그램 원문이 장비 밖으로 나가지 않고 air gap 망에서도 동작합니다. 원격 PlantUML 서버는 `PLANTUML_SERVER_URL`로 명시할 때만 대체 경로로 사용됩니다.
- 실행 조건: 로컬 렌더러(Java + `plantuml.jar` 또는 `plantuml` CLI)나 접근 가능한 PlantUML 서버가 필요합니다. Docker 이미지는 Debian `plantuml` 패키지를 설치하므로 컨테이너는 별도 설정 없이 오프라인 렌더링이 됩니다. Windows 번들은 `tools/plantuml.jar`를 동봉하므로 Java 런타임만 있으면 됩니다.

## 렌더러 선택 순서

실제로 사용 가능한 첫 번째 렌더러를 고르고, 결과를 `config`와 `/healthz`에서 보고합니다.

1. `PLANTUML_JAR_PATH`의 jar이 존재하고 `JAVA_PATH`를 찾을 수 있으면 → `java -Djava.awt.headless=true -jar <jar> -pipe`
2. `PLANTUML_PATH` CLI가 `PATH`에 있으면 → `plantuml -pipe`
3. `PLANTUML_SERVER_URL`이 설정돼 있으면 → 인코딩 후 HTTP로 가져오기
4. 셋 다 없으면 렌더링 도구는 세 가지 선택지를 모두 안내하는 메시지와 함께 실패합니다.

오프라인이 되는 것은 1·2번뿐이며, `config`의 `offlineCapable`로 확인할 수 있습니다.

## 번들 동봉 jar

`scripts/download-plantuml.ps1`이 PlantUML jar를 `vendor/plantuml/plantuml.jar`로 내려받고 릴리스 digest와 SHA256을 대조한 뒤 출처를 `vendor/plantuml/README.txt`에 기록합니다. `publish-mcp-bundle.ps1`이 이를 번들의 `tools/plantuml.jar`로 복사하고 번들 설정의 `PLANTUML_JAR_PATH`가 그 경로를 가리키므로, Windows 번들은 PlantUML을 따로 설치하지 않아도 렌더링됩니다.

PlantUML은 같은 엔진을 여러 라이선스로 배포합니다. 번들이 상용 설치본에 재배포되므로 다운로드 스크립트는 GPL인 `plantuml.jar` 대신 **MIT 에디션**을 기본값으로 받습니다.

```powershell
.\scripts\download-plantuml.ps1                     # MIT 에디션, 최신 릴리스
.\scripts\download-plantuml.ps1 -Edition asl        # Apache-2.0
.\scripts\download-plantuml.ps1 -Version v1.2026.6  # 릴리스 고정
```

jar는 커밋하지 않습니다. OfficeCLI·MATLAB vendor 폴더와 마찬가지로 `vendor/`는 git에서 제외됩니다.

**jar만으로는 실행되지 않습니다.** 번들에 JRE는 포함하지 않으므로 대상 PC의 `PATH`나 `JAVA_PATH`에 Java가 있어야 합니다. Java가 없으면 설정된 `PLANTUML_SERVER_URL`로 넘어가고, 그것도 없으면 `renderer: none`으로 보고합니다.

## 빌드

```powershell
docker build -t local/mcp-plantuml .
```

## Air Gap 내보내기

[airgap/README.ko.md](airgap/README.ko.md)를 참고해 `local/mcp-plantuml:latest`를 `airgap/local-mcp-plantuml.tar`로 내보내고, air gap 장비로 복사한 뒤 `docker load`로 적재해 실행합니다. 이미지가 렌더러를 내장하므로 air gap 쪽에 PlantUML 서버가 따로 필요하지 않습니다.

## 실행

```powershell
docker run --rm -p 127.0.0.1:8100:8080 `
  -v "${PWD}:/workspace" `
  -e "MCP_ALLOWED_DIRS=/workspace" `
  -e "MCP_PATH_MAPPINGS=${PWD}=/workspace" `
  local/mcp-plantuml
```

MCP 클라이언트는 Streamable HTTP `http://localhost:8100/mcp` 또는 레거시 SSE `http://localhost:8100/sse`로 연결합니다.

## 도구

| 도구 | 설명 |
| --- | --- |
| `config` | 선택된 렌더러, 설정된 경로, 오프라인 가능 여부를 보고합니다. |
| `list_formats` | 지원 형식과 각 형식의 반환 방식(text/base64)을 나열합니다. |
| `render_diagram` | PlantUML 원문을 렌더링해 결과를 그대로 반환합니다. |
| `render_source_file` | `.puml` 파일을 읽어 디스크에 쓰지 않고 렌더링합니다. |
| `render_to_file` | 원문을 렌더링해 지정한 출력 경로에 저장합니다. |
| `render_file_to_directory` | `.puml` 파일을 같은 위치나 지정한 출력 폴더에 렌더링합니다. |
| `check_syntax` | 다이어그램을 만들지 않고 문법만 검사합니다. |
| `read_source` | PlantUML 원문 파일을 읽습니다. |
| `encode_url` | 원문을 PlantUML 압축 형식으로 인코딩하고 서버 URL을 만듭니다. |
| `decode_url` | 인코딩 문자열이나 PlantUML URL 전체를 원문으로 되돌립니다. |

지원 형식은 `svg`, `png`, `txt`, `utxt`, `eps`, `latex`입니다. 텍스트 형식은 텍스트로, `png`와 `eps`는 base64로 반환하며 바이트 수를 함께 알려줍니다.

`encode_url`/`decode_url`은 PlantUML의 deflate + 전용 base64 알파벳을 그대로 구현했으므로 어떤 PlantUML 서버와도 호환됩니다.

## 경로·쓰기 보호

`read_source`, `render_source_file`, `render_to_file`, `render_file_to_directory`는 모든 경로를 `MCP_ALLOWED_DIRS`와 `MCP_PATH_MAPPINGS`로 검사합니다. 저장소의 다른 파일 접근 서버와 동일한 방식입니다. 파일을 쓰는 세 도구는 `MCP_ENABLE_PLANTUML_WRITES`로 추가 차단할 수 있습니다.

## 환경변수

| 변수 | 기본값 | 용도 |
| --- | --- | --- |
| `PLANTUML_JAR_PATH` | Docker 빈 값, 번들 `tools/plantuml.jar` | `plantuml.jar` 경로. Java가 있으면 우선 사용합니다. |
| `JAVA_PATH` | `java` | jar 실행에 사용할 Java 실행 파일. |
| `PLANTUML_PATH` | `plantuml` | jar이 없을 때 사용할 PlantUML CLI. |
| `PLANTUML_SERVER_URL` | 빈 값 | 로컬 렌더러가 없을 때만 사용하는 원격 PlantUML 서버. |
| `PLANTUML_INCLUDE_PATH` | 빈 값 | `!include`와 다이어그램 라이브러리 검색 경로. |
| `MCP_ALLOWED_DIRS` | Dockerfile `/`, 번들 `*` | 파일 도구가 접근할 수 있는 루트. |
| `MCP_PATH_MAPPINGS` | 빈 값 | 호스트-컨테이너 경로 매핑. 예: `C:\work=/workspace`. |
| `MCP_ENABLE_PLANTUML_WRITES` | Dockerfile 기본 `true` | `false`로 두면 디스크에 쓰는 도구가 차단됩니다. |

## Kubernetes

Kubernetes 매니페스트는 [k8s/](k8s/README.ko.md)에 있습니다. 이미지가 자체 렌더링하므로 외부 엔드포인트가 필요 없습니다.
