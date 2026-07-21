# MCP-PDF

MCP-PDF는 PDF를 등록하고 구조화된 데이터셋으로 변환한 뒤, 페이지·표·이미지·청크를 MCP를 통해 조회하고 관리하는 서버입니다. LIG AI MCP Server Suite에서는 기본적으로 `42199` 포트를 사용합니다.

이 서버의 주된 목적은 다음 두 가지입니다.

1. PDF를 텍스트, 페이지, 구조 요소, 표, 이미지, 청크 및 메타데이터로 변환하여 RAG 구축에 사용할 데이터셋을 만드는 것
2. 별도 RAG 서버가 없어도 MCP 클라이언트나 LLM이 원문 페이지와 구조화된 데이터를 직접 찾아보고 검증할 수 있게 하는 것

MCP-PDF는 **RAG 답변 생성 서버가 아닙니다.** 검색 결과와 근거 청크는 제공하지만, 프롬프트 조립, 재순위화, LLM 호출 및 최종 답변 생성은 수행하지 않습니다. 별도의 RAG 서버는 MCP-PDF가 내보내거나 저장한 데이터셋을 소비하도록 구성할 수 있습니다.

## 지원 범위

- PDF 등록, SHA-256 중복 판정, 원본 변경 및 누락 감지
- 비동기 작업 큐, 진행률, 취소, 재시도, 이벤트, 경고 및 오류 기록
- Docling Serve 원격 파서와 로컬 Docling CLI 지원
- 텍스트, OCR 정보, 페이지, 제목, heading path, 표, 이미지, caption, bounding box, 읽기 순서 추출
- 문서 구조와 표 경계를 보존하는 결정적 청킹
- 문서·페이지·요소·청크·버전·작업 메타데이터 관리
- SQLite FTS 기반 키워드 검색과 한글/CJK 부분 문자열 fallback
- 선택적 임베딩 생성, 로컬 벡터 검색 및 하이브리드 검색
- 청크 조회, 수정, 삭제, 이웃 청크 연결 및 재청킹
- SQLite, PostgreSQL, Qdrant 저장 작업
- JSONL 및 Parquet 내보내기
- Poppler를 이용한 선택 페이지 PNG 렌더링
- 데이터셋 품질 검사와 처리 경고 조회

지원하지 않는 범위는 다음과 같습니다.

- 외부 RAG 서버 조회 또는 운영
- 질문에 대한 최종 자연어 답변 생성
- LLM 호출, 프롬프트 구성, 대화 메모리 관리
- 외부 reranker를 이용한 검색 결과 재순위화
- PDF 원본 편집

## 전체 설계

```text
PDF 원본
   │
   ├─ 경로·확장자·접근 범위 검사
   ├─ SHA-256 계산 ─────────────── 기존 데이터셋 재사용(dedup)
   │
   ▼
Docling Serve 또는 Docling CLI
   │
   ├─ 페이지 텍스트/OCR
   ├─ title/heading/table/picture/code/formula
   ├─ bounding box/읽기 순서/caption
   └─ Docling 원본 JSON 보존
   │
   ▼
정규화
   │
   ├─ 페이지
   ├─ 구조 요소
   ├─ 표/이미지 artifact
   └─ 경고
   │
   ▼
구조 기반 청킹
   │
   ├─ heading/table 경계 보존
   ├─ page range와 source element 연결
   ├─ previous/next chunk 연결
   └─ 선택적 임베딩
   │
   ▼
SQLite 운영 저장소
   │
   ├─ MCP 직접 열람·검색·관리
   ├─ JSONL/Parquet 내보내기
   ├─ PostgreSQL 저장
   └─ Qdrant 저장
```

### 주요 구성 요소

| 구성 요소 | 역할 |
|---|---|
| `PdfRuntime` | 작업 큐, 전체 수집 파이프라인, 검색, 내보내기 및 외부 저장소 연동을 조정합니다. |
| `PdfParser` | Docling Serve 또는 Docling CLI를 호출하고 결과를 내부 모델로 정규화합니다. |
| `PdfChunker` | heading, 표, 페이지와 토큰 상한을 고려하여 청크를 생성합니다. |
| `PdfStore` | SQLite 스키마, 버전, 작업, 페이지, 요소, 청크, FTS 및 작업 이력을 관리합니다. |
| `PdfAdapters` | OpenAI 호환 임베딩 API, PostgreSQL 및 Qdrant를 연결합니다. |
| `PdfTools` | MCP 클라이언트에 29개 도구를 노출합니다. |

## 수집 처리 과정

`start_pdf_ingest`는 즉시 결과를 반환하지 않고 작업을 큐에 등록합니다. 반환받은 `jobId`로 상태와 이벤트를 조회해야 합니다.

처리 상태는 다음 순서로 진행됩니다.

```text
Queued → Inspecting → Parsing → Normalizing → Chunking
       → Embedding(선택) → Indexing(선택) → Completed/Partial
```

오류나 사용자 요청에 따라 `Failed`, `CancelRequested`, `Canceled` 상태가 될 수 있습니다.

1. 원본 경로가 허용 범위에 있고 `.pdf` 파일인지 확인합니다.
2. SHA-256을 계산해 같은 해시·파서 프로필·청크 프로필의 기존 문서를 찾습니다.
3. 기존 문서가 있고 `force=false`이면 재파싱하지 않고 기존 문서 ID와 데이터셋을 반환합니다.
4. 신규 또는 강제 수집이면 Docling에 PDF를 제출합니다.
5. Docling의 JSON, Markdown 및 텍스트 결과를 페이지와 구조 요소로 정규화합니다.
6. 페이지와 읽기 순서에 따라 요소를 정렬하고 구조 기반 청크를 생성합니다.
7. 요청된 경우 임베딩을 생성합니다.
8. 새 문서 버전과 모든 처리 결과를 하나의 SQLite 트랜잭션으로 저장합니다.
9. `indexTarget`이 있으면 PostgreSQL 또는 Qdrant 저장까지 수행합니다.

### 중복과 버전

- 문서 ID는 원본 절대 경로, 파서 프로필 및 청크 프로필을 기반으로 안정적으로 생성됩니다.
- 같은 내용의 파일을 다시 수집하면 SHA-256 중복 판정으로 기존 데이터셋을 재사용합니다.
- `force=true` 또는 원본 변경 후 재수집하면 같은 문서 ID 아래 새 버전을 생성합니다.
- 일반 조회와 검색은 현재 버전만 사용하며 이전 버전 데이터는 격리됩니다.
- `check_pdf_changes`는 현재 파일 해시와 저장된 해시를 비교해 `unchanged`, `changed`, `missing`을 반환합니다.

## 데이터 모델

SQLite 기본 경로는 `%LOCALAPPDATA%\LIG AI MCP\pdf\mcp-pdf.db`입니다.

| 데이터 | 주요 내용 |
|---|---|
| 문서 | 원본 경로, 파일명, SHA-256, 크기, 제목, 페이지 수, 현재 버전, 상태 |
| 버전 | 파서·청크 프로필과 버전별 처리 상태 |
| 페이지 | 페이지 번호, 추출 텍스트, OCR 적용 여부, confidence |
| 요소 | 유형, 텍스트, heading path, 페이지 범위, bounding box, 읽기 순서, caption, 구조 데이터 |
| 청크 | 본문, 임베딩용 본문, 페이지 범위, heading path, 원본 요소 ID, 이웃 ID, 언어, confidence |
| artifact | 이미지 등 추출 파일의 경로, 페이지, caption 및 media type |
| 작업 | 상태, 단계, 진행률, 처리 페이지, 생성 청크, 오류 및 요청 내용 |
| 경고 | 파서, OCR, 빈 페이지 및 부분 처리 경고 |
| 저장 작업 | SQLite, PostgreSQL, Qdrant 쓰기의 대상, 상태, 레코드 수 및 오류 |

각 청크에는 다음과 같은 출처 정보가 유지됩니다.

- 안정적인 `documentId`, `documentVersion`, `chunkId`
- `pageStart`, `pageEnd`
- `headingPath`, `contentType`
- 원본 `sourceElements`
- `previousChunkId`, `nextChunkId`
- 원본 경로와 SHA-256
- 파서·파서 버전·파서 프로필·청크 프로필
- OCR 적용 여부와 confidence

따라서 검색 결과를 원본 페이지와 구조 요소까지 역추적할 수 있습니다.

## 외부 종속성

> **에어갭 환경:** Docker 이미지 파일만 반입하면 OCR·layout·table 모델이 빠져 있을 수 있습니다. 인터넷 연결 PC에서 Docling을 실제 변환까지 실행해 모델 캐시를 준비한 뒤 이미지와 캐시를 함께 옮겨야 합니다. 전체 절차는 [`AIRGAP.ko.md`](AIRGAP.ko.md)를 참조하십시오.

### Docling

PDF를 처음 파싱하려면 Docling이 필요합니다. Windows 설치 프로그램에는 Docling이 포함되지 않습니다. Docling이 없어도 MCP-PDF 서버 자체는 시작되지만 수집 작업은 명확한 오류와 함께 실패합니다.

권장 구성은 공식 Docling Serve 컨테이너입니다.

```powershell
docker volume create lig-docling-cache

docker run -d `
  --name lig-docling-serve `
  --restart unless-stopped `
  -p 127.0.0.1:5001:5001 `
  -v lig-docling-cache:/opt/app-root/src/.cache `
  -e DOCLING_DEVICE=cpu `
  -e DOCLING_NUM_THREADS=4 `
  -e DOCLING_SERVE_ENG_LOC_NUM_WORKERS=1 `
  -e DOCLING_SERVE_ENABLE_UI=0 `
  quay.io/docling-project/docling-serve:v1.21.0
```

상태 확인:

```powershell
Invoke-RestMethod http://127.0.0.1:5001/health
docker logs --tail 100 lig-docling-serve
```

다른 서버를 사용하려면 `DOCLING_SERVICE_URL`을 변경하고, 인증이 필요하면 `DOCLING_SERVICE_API_KEY`를 설정합니다. 로컬 CLI를 사용하려면 `DOCLING_MODE=local`, `DOCLING_COMMAND=docling`로 설정합니다.

### Poppler

`render_pdf_pages`로 원본 페이지를 PNG로 만들 때만 `pdftoppm` 호환 실행 파일이 필요합니다. 텍스트·표·청크 추출에는 필요하지 않습니다.

Windows LIG Setup에는 검증된 portable Poppler payload가 포함됩니다. 설치 시작 시 `예/아니요/취소`로 선택하며 기본값은 `예`입니다. 선택하면 다음 위치에 설치되고 MCP-PDF가 자동으로 발견합니다.

```text
C:\Program Files\LIG AI MCP\dependencies\poppler\Library\bin\pdftoppm.exe
```

Setup에서 제외했거나 소스 개발 환경이라면 PATH 또는 `PDF_RENDER_COMMAND`의 사용자 지정 절대 경로를 사용할 수 있습니다.

```powershell
winget install --id oschwartz10612.Poppler --exact
pdftoppm -v
```

탐색 우선순위는 사용자 지정 절대 경로, Setup의 내장 Poppler, PATH의 `pdftoppm` 순서입니다. 이전 설치의 기본값 `PDF_RENDER_COMMAND=pdftoppm`이 사용자 환경 파일에 남아 있어도 내장 Poppler를 우선 사용합니다.

### 선택적 서비스

| 서비스 | 필요한 기능 | 필수 설정 |
|---|---|---|
| OpenAI 호환 embedding endpoint | 임베딩 생성, vector/hybrid 검색 | `PDF_EMBEDDING_PROVIDER`, `PDF_EMBEDDING_ENDPOINT`, `PDF_EMBEDDING_MODEL` |
| PostgreSQL | 청크 데이터셋 upsert | `PDF_POSTGRES_CONNECTION_STRING` |
| Qdrant | 임베딩 벡터 upsert | `PDF_QDRANT_URL`, 선택적 API key, 사전 생성된 임베딩 |

.NET 런타임, SQLite 네이티브 라이브러리, JSONL 및 Parquet 처리 라이브러리는 Windows 번들에 포함됩니다.

#### 임베딩 서비스

임베딩 서비스는 청크나 검색 질의를 의미를 나타내는 숫자 벡터로 변환합니다. 단어가 정확히 일치하지 않아도 의미가 유사한 내용을 찾을 때 사용합니다.

```text
"장비 전원을 차단하는 방법"
          │ embedding
          ▼
[0.018, -0.224, 0.517, ...]
```

MCP-PDF는 OpenAI 호환 `/v1/embeddings` 형식의 HTTP endpoint를 사용합니다. OpenAI 인터넷 서비스를 반드시 사용한다는 뜻은 아닙니다. 에어갭에서는 Ollama, vLLM, LocalAI 또는 조직에서 운영하는 호환 서버처럼 모델을 로컬에서 실행하는 구성을 사용할 수 있습니다.

```text
PDF_EMBEDDING_PROVIDER=openai-compatible
PDF_EMBEDDING_ENDPOINT=http://127.0.0.1:11434/v1/embeddings
PDF_EMBEDDING_MODEL=nomic-embed-text
PDF_EMBEDDING_API_KEY=
```

`PDF_EMBEDDING_PROVIDER=none`이면 임베딩 기능이 비활성화됩니다. provider 문자열은 현재 구현에서 endpoint 사용 여부를 구분하기 위한 값이므로 `none`이 아닌 값을 지정하면 OpenAI 호환 요청을 보냅니다.

임베딩은 다음 방법으로 생성합니다.

- `start_pdf_ingest`에서 `generateEmbeddings=true`
- 이미 수집된 문서에 `generate_pdf_embeddings`

한 번에 최대 64개 청크를 묶어 endpoint에 요청하고, 반환된 벡터를 현재 SQLite 청크에 저장합니다. 임베딩 모델을 바꾸거나 청크를 수정·재청킹하면 전체 임베딩을 다시 생성해야 합니다. 서로 다른 모델이 만든 벡터는 차원과 의미 공간이 다를 수 있으므로 섞어 검색해서는 안 됩니다.

임베딩이 있으면 다음 검색을 사용할 수 있습니다.

- `vector`: 질의 벡터와 SQLite에 저장된 청크 벡터의 cosine similarity 계산
- `hybrid`: 키워드 순위와 벡터 순위를 결합

임베딩이 없을 때 `hybrid`는 자동으로 키워드 검색 결과를 반환합니다. 현재 MCP-PDF의 vector 검색은 SQLite에서 임베딩된 청크를 읽어 애플리케이션 내부에서 계산하므로 중간 규모 문서 집합에는 간단하지만, 매우 큰 데이터셋의 고성능 검색은 별도 RAG 서버와 Qdrant 같은 vector DB가 더 적합합니다.

에어갭에서 임베딩을 사용하려면 서버 프로그램만 아니라 다음 항목도 함께 반입해야 합니다.

- embedding serving 프로그램 또는 Docker 이미지
- 사용할 embedding 모델 파일과 tokenizer
- 모델 라이선스와 버전 기록
- CPU/GPU 실행 의존성
- 대표 한글·영문 질의의 검색 품질 검증 결과

#### PostgreSQL

PostgreSQL은 여러 프로그램이나 사용자가 청크 데이터셋을 공유해야 할 때 사용하는 관계형 DB입니다. `save_pdf_dataset(provider="postgresql")`을 호출하면 MCP-PDF가 다음 객체를 자동 생성하고 청크를 upsert합니다.

```text
mcp_pdf_chunks 테이블
├─ chunk_id 기본키
├─ document_id / document_version / chunk_index
├─ text_content / embedding_text / title
├─ heading_path JSONB
├─ content_type / page_start / page_end / token_count
├─ 전체 청크 metadata JSONB
└─ updated_at
```

문서·버전·청크 순서 index와 `simple` 구성의 PostgreSQL full-text GIN index도 생성합니다. 같은 `chunk_id`를 다시 저장하면 현재 내용으로 갱신됩니다.

현재 PostgreSQL adapter는 다음 성격입니다.

- MCP-PDF SQLite 운영 DB를 대체하지 않습니다.
- MCP-PDF가 PostgreSQL에서 데이터를 다시 읽어 검색하지 않습니다.
- PostgreSQL은 외부 RAG 서버나 ETL 작업을 위한 데이터셋 전달 대상입니다.
- `pgvector` column이나 vector index를 자동 생성하지 않습니다.
- 임베딩 값이 전체 metadata JSON에 포함될 수는 있지만 PostgreSQL vector 검색용 schema는 아닙니다.

따라서 관계형 필터, 문서 관리, SQL 조회와 다른 업무 시스템 연계가 필요할 때 적합합니다. vector 검색까지 PostgreSQL에서 수행하려면 별도 RAG 서버가 `pgvector` schema와 index를 설계해야 합니다.

설정 예시:

```text
PDF_POSTGRES_CONNECTION_STRING=Host=127.0.0.1;Port=5432;Database=rag;Username=rag_writer;Password=...
```

에어갭에서는 PostgreSQL 설치 패키지 또는 고정 Docker 이미지, 데이터 volume, 계정·인증서, backup/restore 절차를 별도로 준비해야 합니다.

#### Qdrant

Qdrant는 임베딩 벡터의 근접 검색을 위한 vector DB입니다. 대규모 청크에서 의미적으로 가까운 결과를 빠르게 찾는 데 사용합니다.

`save_pdf_dataset(provider="qdrant", target="collection-name")`을 호출하면 MCP-PDF는 다음을 수행합니다.

1. 임베딩이 있는 현재 청크만 선택합니다.
2. embedding 차원과 cosine distance를 사용하는 collection을 생성합니다.
3. 이미 collection이 있으면 재사용합니다.
4. 100개 단위 batch로 vector와 payload를 upsert합니다.

payload에는 `chunkId`, 문서·버전·순서, 본문, 제목, heading path, content type, 페이지 범위, 토큰 수와 원본 경로가 포함됩니다. `target`을 생략하면 collection 이름은 `mcp_pdf_chunks`입니다.

```text
PDF_QDRANT_URL=http://127.0.0.1:6333
PDF_QDRANT_API_KEY=
```

Qdrant 저장 전에는 반드시 `generate_pdf_embeddings`를 실행해야 합니다. embedding 모델을 변경하면 기존 collection을 그대로 섞어 쓰지 말고 새 collection을 만들거나 전체 vector를 교체해야 합니다.

현재 Qdrant adapter의 중요한 경계는 다음과 같습니다.

- MCP-PDF는 Qdrant에 vector와 payload를 씁니다.
- MCP-PDF의 `search_pdf_content`는 현재 Qdrant를 조회하지 않습니다.
- 실제 Qdrant 검색, metadata filter, rerank와 답변 생성은 별도 RAG 서버의 역할입니다.

에어갭에서는 Qdrant Docker 이미지 또는 설치 파일과 데이터 volume을 반입하고, 내부 접근 제어와 backup 정책을 구성해야 합니다.

#### 어떤 구성을 선택해야 하는가

| 목적 | 필요한 선택 기능 |
|---|---|
| PDF 등록·청킹·키워드 검색·열람 | 아무것도 추가하지 않음. SQLite만 사용 |
| 페이지를 PNG로 시각 확인 | Poppler 추가 |
| MCP-PDF 안에서 의미 검색 | 로컬 embedding 서비스 추가 |
| 청크를 SQL 기반 RAG/업무시스템에 전달 | PostgreSQL 추가 |
| 별도 RAG 서버에서 대규모 vector 검색 | embedding 서비스와 Qdrant 추가 |
| 데이터 파일만 다른 시스템으로 전달 | JSONL/Parquet 사용, 외부 서비스 불필요 |

PostgreSQL과 Qdrant를 모두 설치할 필요는 없습니다. 별도 RAG 서버가 자체 DB와 embedding pipeline을 갖는다면 MCP-PDF에서는 임베딩도 생성하지 않고 JSONL 또는 Parquet만 전달할 수 있습니다.

현재처럼 MCP-PDF를 먼저 PDF 데이터 준비·검증 도구로 사용하는 단계에서는 다음 최소 구성이 가장 단순합니다.

```text
필수: MCP-PDF + Docling + SQLite 내장
권장: Poppler
보류 가능: embedding service, PostgreSQL, Qdrant
```

외부 저장 작업의 성공·실패, 대상과 레코드 수는 `list_pdf_storage_operations`로 확인할 수 있습니다.

### CPU, GPU와 모델 실행 특성

Docling의 OCR, Layout, Table 및 Figure 모델은 GPU가 필수인 모델이 아닙니다. CPU만으로도 전체 기능을 실행할 수 있으며 GPU는 대량 처리나 스캔 OCR의 처리시간을 줄이기 위한 선택 사항입니다.

현재 검증 환경에서는 다음 CPU 설정으로 실제 90페이지 기술문서를 처리했습니다.

```text
DOCLING_DEVICE=cpu
DOCLING_NUM_THREADS=4
DOCLING_SERVE_ENG_LOC_NUM_WORKERS=1

결과: 90페이지, 표 70개, 청크 327개
최초 처리: 약 9분
메모리: 대략 2.4~3.5GiB
```

이 측정은 텍스트 레이어가 있는 디지털 영문 PDF 기준입니다. 페이지 전체가 이미지인 스캔 PDF, 강제 OCR, 복잡한 표·수식 및 대량 동시 작업은 더 오래 걸리고 메모리도 더 사용할 수 있습니다.

모델 cache 크기와 실행 메모리는 같은 값이 아닙니다. cache는 디스크에 보관되는 범용 가중치 모음이며 Docling은 처리 단계에 필요한 모델을 메모리에 적재합니다. 또한 PDF 처리가 끝난 뒤 페이지·청크·표를 SQLite에서 검색하는 과정에는 Docling 추론이 다시 필요하지 않습니다.

| 환경 | 권장 시작점 | 설명 |
|---|---|---|
| 일반 노트북 | CPU, thread 4, Docling worker 1, MCP worker 1 | 안정성과 메모리 절약 우선 |
| 다코어 워크스테이션 | CPU, 물리 코어에 맞춘 thread, worker 1부터 증가 | 실제 문서로 메모리와 처리량 측정 필요 |
| NVIDIA GPU PC | CUDA, Docling worker 1 | driver, Docker GPU 지원과 이미지 호환성 확인 필요 |
| 대량 처리 서버 | GPU 또는 고성능 CPU, 복수 worker, queue 제한 | 동시성·메모리·장애 복구를 부하 시험으로 결정 |

`DOCLING_DEVICE=auto`는 사용 가능한 장치를 자동 선택하지만, 재현성이 중요한 에어갭 운영 환경에서는 `cpu` 또는 `cuda`를 명시하는 편이 좋습니다.

### Docling 성능 조정 환경 변수

Docling Serve는 MCP-PDF와 별도 프로세스이므로 성능 변수는 `mcp-pdf.env`가 아니라 **Docling 컨테이너를 실행할 때** `docker run -e`로 전달해야 합니다.

#### 핵심 변수

| 환경 변수 | 현재값 | 역할과 주의사항 |
|---|---:|---|
| `DOCLING_DEVICE` | `cpu` | `auto`, `cpu`, `cuda`, `cuda:N`, `mps`, `xpu` 중 하나입니다. |
| `DOCLING_NUM_THREADS` | `4` | 한 추론 작업에서 사용할 CPU thread입니다. 물리 코어 수를 시작점으로 삼되 worker 수와 곱한 총 부하를 확인합니다. |
| `OMP_NUM_THREADS` | `4` | `DOCLING_NUM_THREADS`가 없을 때 사용할 대체값입니다. 혼선을 피하려면 두 값을 같게 하거나 `DOCLING_NUM_THREADS`만 관리합니다. |
| `DOCLING_SERVE_ENG_LOC_NUM_WORKERS` | `1` | 동시에 변환을 수행하는 로컬 engine worker 수입니다. 늘리면 처리량과 메모리 사용량이 함께 증가할 수 있습니다. |
| `DOCLING_SERVE_ENG_LOC_SHARE_MODELS` | `false` | 복수 worker의 모델 공유 설정입니다. 변경 시 실제 문서와 사용하는 backend에서 메모리·안정성을 검증해야 합니다. |
| `DOCLING_SERVE_LOAD_MODELS_AT_BOOT` | `true` | 시작할 때 모델을 적재합니다. 에어갭에서는 누락 모델을 조기에 발견하는 데 유리합니다. |
| `DOCLING_SERVE_OPTIONS_CACHE_SIZE` | `2` | 서로 다른 변환 옵션에 대한 pipeline cache 수입니다. 프로필이 많으면 재사용성이 높아지지만 메모리가 늘 수 있습니다. |

#### 처리량과 보호 한계

| 환경 변수 | 기본 동작 | 역할 |
|---|---|---|
| `DOCLING_SERVE_OCR_BATCH_SIZE` | 자동 | OCR batch 크기. 크게 하면 처리량이 좋아질 수 있지만 메모리 사용량도 증가합니다. |
| `DOCLING_SERVE_LAYOUT_BATCH_SIZE` | 자동 | Layout 분석 batch 크기 |
| `DOCLING_SERVE_TABLE_BATCH_SIZE` | 자동 | Table 분석 batch 크기 |
| `DOCLING_SERVE_BATCH_POLLING_INTERVAL_SECONDS` | 자동 | batch queue polling 간격 |
| `DOCLING_SERVE_QUEUE_MAX_SIZE` | 제한 없음 | 대기 queue 상한. 다중 사용자 서버의 과부하 방지에 사용합니다. |
| `DOCLING_SERVE_MAX_NUM_PAGES` | 사실상 제한 없음 | 요청 한 건의 최대 페이지 수 |
| `DOCLING_SERVE_MAX_FILE_SIZE` | 사실상 제한 없음 | 요청 파일 크기 상한 |
| `DOCLING_SERVE_MAX_DOCUMENT_TIMEOUT` | 7일 | 문서가 요청할 수 있는 최대 처리 제한 시간 |
| `DOCLING_SERVE_MAX_SYNC_WAIT` | 120초 | 동기 API 최대 대기. MCP-PDF의 기본 비동기 API 사용 시 중요도가 낮습니다. |

batch 크기는 GPU/CPU와 모델별 최적값이 다르므로 임의로 크게 고정하지 말고 기본 자동값에서 시작하는 것이 안전합니다. 단일 노트북에서 가장 영향이 큰 값은 `DOCLING_NUM_THREADS`와 `DOCLING_SERVE_ENG_LOC_NUM_WORKERS`입니다.

Docker 자체 자원 한도는 환경 변수가 아닙니다. 필요하면 다음과 같은 실행 옵션으로 별도 제한합니다.

```powershell
docker run ... --cpus 4 --memory 8g ...
```

NVIDIA GPU를 사용할 때는 최소한 다음 조건이 필요합니다.

- 호환 NVIDIA GPU와 driver
- Docker Desktop/Engine의 GPU passthrough 지원
- 컨테이너 안에서 CUDA를 사용할 수 있는 이미지와 PyTorch build
- `docker run --gpus all`
- `DOCLING_DEVICE=cuda` 또는 특정 장치의 `cuda:0`

현재 검증한 `docling-serve:v1.21.0` 이미지의 PyTorch는 CUDA 지원 build이지만, 실제 GPU 사용 가능 여부는 대상 PC의 driver와 Docker 구성에 따라 달라집니다. `DOCLING_DEVICE=cuda`만 설정한다고 GPU가 자동으로 생기지는 않습니다.

MCP-PDF의 `PDF_MAX_CONCURRENT_JOBS`도 함께 조정해야 합니다. 메모리가 작은 PC에서는 MCP-PDF worker와 Docling worker를 모두 1로 시작합니다. 고성능 PC에서는 Docling worker를 먼저 늘리고, 실제 최대 크기 PDF로 안정성을 확인한 뒤 MCP-PDF 동시 작업 수를 맞춥니다.

### 에어갭 구성 요약

```text
[인터넷 연결 준비 PC]
  1. Docling/보조 Docker 이미지 다운로드
  2. 실제 디지털 PDF와 스캔 PDF로 모델 사전 준비
  3. Docling 이미지, 모델 캐시, Poppler를 내장한 Setup을 키트로 묶음
  4. 모든 파일의 SHA-256 manifest 생성
                     │
                     ▼ 승인된 반입 매체
[에어갭 PC]
  5. 해시 검증
  6. Docker 이미지 load 및 모델 cache volume 복원
  7. Docling을 127.0.0.1:5001에서 실행
  8. MCP-PDF 환경 변수 설정 및 재시작
  9. health/config/실제 PDF 수집 검증
```

에어갭 배포 키트에는 최소한 다음 항목이 필요합니다.

- LIG AI MCP Windows Setup
- `quay.io/docling-project/docling-serve:v1.21.0`의 `docker save` 파일
- 사전 준비된 Docling 모델 캐시 archive
- 캐시 복구용 보조 컨테이너 이미지 또는 조직에서 승인한 동등한 도구
- Docker Desktop이 대상 PC에 없다면 승인된 오프라인 설치 파일
- 파일명, 버전, 크기와 SHA-256을 기록한 manifest

반입 후 MCP-PDF는 기본적으로 로컬 Docling 주소 `http://127.0.0.1:5001`에 연결합니다. 인터넷 연결은 필요하지 않지만, 준비 단계에서 사용하지 않은 모델이 런타임에 추가 다운로드되지 않도록 **에어갭 반입 전에 네트워크를 차단한 상태로 대표 PDF 수집을 한 번 더 검증**하는 것을 권장합니다.

## 설정

전체 예시는 [`config/pdf.env.example`](config/pdf.env.example)에 있습니다.

### 기본 설정

| 환경 변수 | 기본값 | 설명 |
|---|---|---|
| `PDF_DATA_DIR` | `%LOCALAPPDATA%\LIG AI MCP\pdf` | DB, artifact, 렌더링 및 내보내기 기본 디렉터리 |
| `PDF_JOB_DB` | `%PDF_DATA_DIR%\mcp-pdf.db` | SQLite DB 파일 |
| `MCP_ALLOWED_DIRS` | `*` | 읽을 수 있는 원본 PDF 경로. `*`는 준비된 모든 드라이브를 의미합니다. |
| `MCP_ENABLE_PDF_WRITES` | `true` | 수집, 취소, 삭제, 수정, 재청킹, 임베딩 및 저장 변경 허용 여부 |
| `PDF_MAX_CONCURRENT_JOBS` | `2` | 동시 작업 worker 수, 허용 범위 1–128 |
| `PDF_JOB_TIMEOUT_SECONDS` | `86400` | 작업 제한 시간, 허용 범위 60–2,592,000초 |
| `PDF_DEFAULT_PROFILE` | `balanced-ko` | 기본 파서 프로필 |
| `PDF_DEFAULT_CHUNK_PROFILE` | `rag-default` | 기본 청크 프로필 |

`MCP_ALLOWED_DIRS`에 여러 경로를 지정할 때는 세미콜론이나 쉼표로 구분합니다.

```text
MCP_ALLOWED_DIRS=C:\Documents;D:\Data;E:\Archive
```

### Docling 설정

| 환경 변수 | 기본값 | 설명 |
|---|---|---|
| `DOCLING_MODE` | `remote` | `remote` 또는 `local` |
| `DOCLING_SERVICE_URL` | `http://127.0.0.1:5001` | Docling Serve 주소 |
| `DOCLING_SERVICE_API_KEY` | 빈 값 | 원격 서비스 인증 키 |
| `DOCLING_COMMAND` | `docling` | 로컬 CLI 명령 또는 절대 경로 |
| `DOCLING_USE_ASYNC` | `true` | 비동기 API 제출·polling·result 조회 사용 |
| `DOCLING_POLL_INTERVAL_SECONDS` | `2` | 비동기 작업 polling 간격, 1–60초 |

비동기 endpoint가 404 또는 405를 반환하는 구형 서비스에서는 동기 변환 API로 자동 fallback합니다.

### 임베딩과 저장소 설정

| 환경 변수 | 기본값 | 설명 |
|---|---|---|
| `PDF_EMBEDDING_PROVIDER` | `none` | `none`이 아니면 OpenAI 호환 embedding 사용 |
| `PDF_EMBEDDING_ENDPOINT` | `http://127.0.0.1:11434/v1/embeddings` | embedding endpoint |
| `PDF_EMBEDDING_API_KEY` | 빈 값 | Bearer API key |
| `PDF_EMBEDDING_MODEL` | `nomic-embed-text` | embedding 모델 이름 |
| `PDF_POSTGRES_CONNECTION_STRING` | 빈 값 | PostgreSQL 연결 문자열 |
| `PDF_QDRANT_URL` | `http://127.0.0.1:6333` | Qdrant 주소 |
| `PDF_QDRANT_API_KEY` | 빈 값 | Qdrant API key |
| `PDF_RENDER_COMMAND` | 자동 | 사용자 지정 명령 또는 절대 경로. 비어 있으면 Setup의 내장 Poppler를 찾고, 없으면 PATH의 `pdftoppm`을 사용합니다. |

## 파서 프로필

| 프로필 | OCR | 표 | 이미지 | 코드/수식 보강 | 권장 용도 |
|---|---|---|---|---|---|
| `fast` | 끔 | 추출 | 추출 | 끔 | 텍스트 레이어가 확실하고 속도가 중요한 PDF |
| `balanced-ko` | 자동, `kor+eng` | 정확 | 추출 | 끔 | 일반적인 한글·영문 혼합 문서의 기본값 |
| `accurate-ko` | 자동, `kor+eng` | 정확 | 추출 | 코드·수식 켬 | 표, 수식, 복잡한 구조의 정확도가 중요한 문서 |
| `scanned-ko` | 강제, `kor+eng` | 정확 | 추출 | 끔 | 텍스트 레이어가 없거나 품질이 낮은 스캔 PDF |

OCR은 “그림이 있는가”가 아니라 “페이지 텍스트를 직접 추출할 수 있는가”에 따라 결정됩니다. 디지털 생성 PDF는 그림이 있어도 OCR이 0페이지일 수 있습니다. 반대로 페이지 전체가 스캔 이미지라면 `scanned-ko`가 적합합니다.

### 프로필 선택 기준

1. 먼저 `balanced-ko`로 처리합니다.
2. 빈 페이지나 텍스트 누락이 있으면 `scanned-ko`로 비교합니다.
3. 표 구조, 코드 또는 수식 품질이 부족하면 `accurate-ko`를 사용합니다.
4. 텍스트 레이어가 정상이고 처리량이 중요하면 `fast`를 사용합니다.
5. 처리 후 반드시 `validate_pdf_dataset`과 `get_pdf_processing_warnings`를 확인합니다.

## 청크 프로필

토큰 수는 모델 tokenizer의 정확한 토큰 수가 아니라 안정적인 내부 추정치입니다.

| 프로필 | 목표 | 최대 | 최소 | 앞/뒤 문맥 | 권장 용도 |
|---|---:|---:|---:|---:|---|
| `rag-small` | 400 | 650 | 80 | 70/30 | 세밀한 검색, 짧은 컨텍스트 모델 |
| `rag-default` | 700 | 1,000 | 120 | 100/40 | 일반 RAG 데이터셋의 기본값 |
| `rag-large` | 1,100 | 1,600 | 200 | 140/60 | 긴 문맥을 유지해야 하는 기술 문서 |

기본 프로필은 peer 요소 병합, heading 경계 보존, 표 경계 보존을 사용합니다. `config/profiles.json`의 `profiles`와 `chunkProfiles` 배열에 같은 이름의 항목을 추가하면 기본값을 덮어쓸 수 있습니다.

## 권장 사용 프로세스

### 1. 서비스 상태 확인

먼저 `config`를 호출합니다. 다음 항목을 확인합니다.

- parser health가 사용 가능한지
- `doclingMode`와 서비스 URL이 의도한 값인지
- 원본 경로가 `allowedDirectories` 안에 있는지
- 기본 파서·청크 프로필이 맞는지
- renderer, embedding, PostgreSQL 등 선택 기능이 준비됐는지

HTTP 상태 확인은 다음 endpoint를 사용할 수 있습니다.

```text
GET http://127.0.0.1:42199/healthz
```

MCP endpoint는 다음과 같습니다.

```text
http://127.0.0.1:42199/mcp
```

### 2. PDF 등록

LLM 또는 MCP 클라이언트에서 다음 인수로 `start_pdf_ingest`를 호출합니다.

```json
{
  "source": "D:\\documents\\manual.pdf",
  "profile": "balanced-ko",
  "chunkProfile": "rag-default",
  "force": false,
  "generateEmbeddings": false,
  "indexTarget": null
}
```

반환된 `jobId`를 보관합니다. 대용량 PDF는 Docling 처리에 수 분 이상 걸릴 수 있습니다.

### 3. 작업 감시

- `get_pdf_job_status(jobId)`로 상태와 진행률을 확인합니다.
- `get_pdf_job_events(jobId)`로 단계 전환과 오류 원인을 확인합니다.
- 더 이상 필요하지 않은 작업은 `cancel_pdf_job(jobId)`로 취소합니다.
- 서버 재시작 시 DB에 남은 `Queued` 작업은 다시 큐에 등록됩니다.

작업이 완료되면 상태 응답의 `documentId`를 이후 호출에 사용합니다.

### 4. 품질 확인

다음 순서를 권장합니다.

1. `get_pdf_document`로 페이지 수와 버전을 확인합니다.
2. `validate_pdf_dataset`으로 빈 페이지, 짧은/과대/중복 청크, OCR 페이지와 경고를 확인합니다.
3. `get_pdf_processing_warnings`로 파서와 페이지별 경고를 확인합니다.
4. `get_pdf_toc`로 제목 계층과 페이지 연결을 확인합니다.
5. `read_pdf_pages`로 첫·중간·마지막 페이지를 직접 읽습니다.
6. `get_pdf_tables`와 `get_pdf_images`로 구조 요소를 확인합니다.
7. 필요하면 `render_pdf_pages`로 원본 페이지 PNG를 만들어 시각적으로 비교합니다.

`get_pdf_images`가 0건이라고 해서 OCR이 실패한 것은 아닙니다. 독립 래스터 이미지 artifact가 없거나 도형이 벡터 요소인 문서일 수 있습니다.

### 5. 검색과 정보 열람

`search_pdf_content`의 mode는 다음과 같습니다.

| mode | 동작 |
|---|---|
| `keyword` | SQLite FTS와 부분 문자열 검색만 사용합니다. 임베딩 서비스가 필요 없습니다. |
| `vector` | 질의를 임베딩하고 저장된 청크 벡터와 cosine similarity를 계산합니다. |
| `hybrid` | 키워드와 벡터 결과를 결합합니다. 임베딩이 없으면 자동으로 키워드 결과를 사용합니다. |

`find_pdf_sources`는 LLM이 답변 근거로 사용하기 적합한 청크를 반환하지만 최종 답변은 생성하지 않습니다.

검색 결과를 받은 뒤에는 `get_pdf_chunk`로 다음 정보를 확인하는 것이 좋습니다.

- 본문과 임베딩용 본문
- 제목과 heading path
- 원본 페이지 범위
- 이전/다음 청크 ID
- 원본 요소 ID
- OCR·confidence 및 파서 메타데이터

정확한 문맥이 더 필요하면 이웃 청크와 `read_pdf_pages`의 원본 페이지를 함께 조회합니다.

### 6. 수정과 재청킹

- `update_pdf_chunk`는 청크 본문과 embedding text를 변경하고 기존 임베딩을 무효화합니다.
- `delete_pdf_chunk`는 SQLite와 키워드 검색 index에서 해당 청크를 삭제합니다.
- `rechunk_pdf`는 저장된 구조 요소를 사용하므로 Docling을 다시 호출하지 않습니다.
- 청크 수정이나 재청킹 후 임베딩을 사용한다면 `generate_pdf_embeddings`를 다시 호출해야 합니다.

`retry_pdf_pages`는 페이지 범위를 검증하지만 현재 구현은 선택 페이지만 부분 갱신하지 않습니다. 문서 ID와 버전 이력을 보존하면서 지정 프로필로 **전체 문서를 안전하게 다시 구축**합니다.

### 7. 저장과 내보내기

SQLite는 기본 운영 저장소이므로 별도 서비스가 필요 없습니다.

```json
{
  "documentId": "doc_...",
  "provider": "postgresql",
  "target": null
}
```

Qdrant 저장 전에는 반드시 임베딩을 생성해야 합니다. `target`은 Qdrant collection 이름이며 생략하면 `mcp_pdf_chunks`를 사용합니다.

JSONL 또는 Parquet 내보내기 예시:

```json
{
  "documentId": "doc_...",
  "format": "jsonl",
  "destination": "D:\\rag-data\\manual"
}
```

`destination`을 생략하면 `%PDF_DATA_DIR%\exports\<documentId>` 아래에 저장됩니다. JSONL은 한 줄에 한 청크를 기록합니다.

### 8. 변경 감지와 정리

- 주기적으로 `check_pdf_changes`를 호출해 원본의 변경·누락 여부를 확인합니다.
- 변경된 원본을 다시 처리하려면 `force=true`로 수집합니다.
- `delete_pdf_document`는 관리 중인 페이지, 요소, 청크, artifact, 경고 및 검색 index를 삭제하지만 **원본 PDF 파일은 삭제하지 않습니다.**

## MCP 도구 목록

### 설정과 작업 관리

| 도구 | 변경 | 설명 |
|---|---:|---|
| `config` | 아니요 | 설정, 파서 상태, 프로필, 저장소와 선택 종속성 상태를 반환합니다. |
| `start_pdf_ingest` | 예 | PDF 수집 작업을 큐에 등록합니다. |
| `get_pdf_job_status` | 아니요 | 작업 상태, 단계, 진행률, 페이지·청크 수와 오류를 조회합니다. |
| `list_pdf_jobs` | 아니요 | 최근 작업을 상태 조건과 함께 조회합니다. |
| `get_pdf_job_events` | 아니요 | 작업 단계, 경고와 오류 이벤트를 조회합니다. |
| `cancel_pdf_job` | 예 | 실행 또는 대기 중인 작업의 취소를 요청합니다. |
| `retry_pdf_pages` | 예 | 지정 페이지 범위를 검증한 뒤 전체 문서를 안전하게 재처리합니다. |

### 문서와 원문 조회

| 도구 | 변경 | 설명 |
|---|---:|---|
| `list_pdf_documents` | 아니요 | 제목, 파일명 또는 원본 경로로 문서를 찾습니다. |
| `check_pdf_changes` | 아니요 | 현재 파일 해시와 저장된 해시를 비교합니다. |
| `get_pdf_document` | 아니요 | 문서 메타데이터와 현재 버전을 반환합니다. |
| `delete_pdf_document` | 예 | 관리 데이터셋을 삭제하며 원본 PDF는 유지합니다. |
| `get_pdf_toc` | 아니요 | heading 요소로 추론한 목차를 반환합니다. |
| `read_pdf_pages` | 아니요 | `1-5,8` 형식의 페이지 범위에서 추출 텍스트를 읽습니다. |
| `get_pdf_tables` | 아니요 | 전체 또는 특정 페이지의 표와 구조 데이터를 반환합니다. |
| `get_pdf_images` | 아니요 | 추출된 이미지 artifact와 caption을 반환합니다. |
| `render_pdf_pages` | 아니요 | 선택 페이지를 PNG로 렌더링합니다. |

### 청크와 검색

| 도구 | 변경 | 설명 |
|---|---:|---|
| `list_pdf_chunks` | 아니요 | offset, limit 및 content type으로 청크를 조회합니다. |
| `get_pdf_chunk` | 아니요 | 청크 본문, 페이지, 구조, 이웃 및 처리 메타데이터를 반환합니다. |
| `update_pdf_chunk` | 예 | 청크 본문을 수정하고 기존 임베딩을 무효화합니다. |
| `delete_pdf_chunk` | 예 | 청크와 해당 키워드 index 항목을 삭제합니다. |
| `rechunk_pdf` | 예 | PDF 재파싱 없이 저장 요소로 청크를 다시 만듭니다. |
| `search_pdf_content` | 아니요 | keyword, vector 또는 hybrid 검색을 수행합니다. |
| `find_pdf_sources` | 아니요 | LLM의 근거로 사용할 검색 결과를 반환합니다. |

### 검증, 임베딩과 외부 저장

| 도구 | 변경 | 설명 |
|---|---:|---|
| `validate_pdf_dataset` | 아니요 | 페이지 범위, 청크 크기, 중복, OCR 및 경고를 검사합니다. |
| `get_pdf_processing_warnings` | 아니요 | 파서·OCR·빈 페이지 등의 경고를 반환합니다. |
| `generate_pdf_embeddings` | 예 | 모든 현재 청크의 임베딩을 생성하거나 재생성합니다. |
| `save_pdf_dataset` | 예 | SQLite, PostgreSQL 또는 Qdrant에 데이터셋을 기록합니다. |
| `list_pdf_storage_operations` | 아니요 | 외부 저장 작업의 성공·실패와 오류를 조회합니다. |
| `export_pdf_dataset` | 예 | 현재 청크를 JSONL 또는 Parquet 파일로 내보냅니다. |

## 저장 디렉터리

기본 구조는 다음과 같습니다.

```text
%LOCALAPPDATA%\LIG AI MCP\pdf\
├─ mcp-pdf.db
├─ mcp-pdf-server.log
├─ documents\
│  └─ <documentId>\
│     └─ v<version>\
│        ├─ docling-document.json
│        ├─ ingest-manifest.json
│        └─ rendered\
│           └─ page-000001.png
└─ exports\
   └─ <documentId>\
      ├─ <filename>.chunks.jsonl
      └─ <filename>.chunks.parquet
```

버전에 따라 Docling이 반환한 추가 artifact가 문서 버전 디렉터리에 저장될 수 있습니다.

## 실행 방법

### LIG 번들

LIG AI MCP Manager가 `McpPdf.exe`를 `42199` 포트에서 실행합니다. 번들 기본 설정은 다음과 같습니다.

```text
MCP_ALLOWED_DIRS=*
MCP_ENABLE_PDF_WRITES=true
DOCLING_MODE=remote
DOCLING_SERVICE_URL=http://127.0.0.1:5001
DOCLING_USE_ASYNC=true
PDF_EMBEDDING_PROVIDER=none
PDF_EMBEDDING_ENDPOINT=http://127.0.0.1:11434/v1/embeddings
PDF_EMBEDDING_MODEL=nomic-embed-text
PDF_QDRANT_URL=http://127.0.0.1:6333
```

### 소스 개발 실행

```powershell
.\mcp-pdf\scripts\run-dev.ps1
```

또는 직접 실행할 수 있습니다.

```powershell
$env:ASPNETCORE_URLS = 'http://127.0.0.1:42199'
$env:DOCLING_SERVICE_URL = 'http://127.0.0.1:5001'
dotnet run --project .\mcp-pdf\src\McpPdf.csproj -c Release
```

### Docker로 MCP-PDF 실행

[`Dockerfile`](Dockerfile)은 `poppler-utils`를 포함합니다. PDF 원본과 데이터 디렉터리를 별도 volume으로 연결해야 합니다.

```powershell
docker build -t lig-mcp-pdf .\mcp-pdf

docker run --rm `
  -p 127.0.0.1:42199:8080 `
  -v lig-mcp-pdf-data:/data `
  -v D:\documents:/documents:ro `
  -e MCP_ALLOWED_DIRS=/documents `
  -e DOCLING_SERVICE_URL=http://host.docker.internal:5001 `
  lig-mcp-pdf
```

## 운영과 장애 대응

### 수집이 바로 실패하는 경우

1. `config`의 parser health를 확인합니다.
2. `Invoke-RestMethod http://127.0.0.1:5001/health`로 Docling을 확인합니다.
3. `get_pdf_job_events`에서 실제 실패 단계를 확인합니다.
4. 원본 경로가 `MCP_ALLOWED_DIRS`에 포함되는지 확인합니다.
5. 대형 문서라면 `PDF_JOB_TIMEOUT_SECONDS`와 Docker 메모리·디스크를 확인합니다.

### 빈 페이지나 글자 누락이 있는 경우

- 텍스트 레이어가 없는 스캔 문서는 `scanned-ko`로 다시 처리합니다.
- 일부 페이지만 문제여도 현재 `retry_pdf_pages`는 전체 문서를 재처리합니다.
- 처리 후 `ocrPages`, 빈 페이지, warning과 원본 렌더링을 함께 비교합니다.

### 표나 구조가 좋지 않은 경우

- `accurate-ko`로 재수집합니다.
- `get_pdf_tables`의 `StructuredData`, 페이지와 heading path를 확인합니다.
- Docling JSON과 원본 렌더링을 비교합니다.

### 렌더링이 실패하는 경우

- `pdftoppm -v`가 새 터미널에서 실행되는지 확인합니다.
- PATH 갱신이 안 됐다면 앱을 다시 시작합니다.
- 필요하면 `PDF_RENDER_COMMAND`에 절대 경로를 지정합니다.

### vector 검색이 실패하는 경우

- `PDF_EMBEDDING_PROVIDER`가 `none`이 아닌지 확인합니다.
- endpoint, 모델명과 API key를 확인합니다.
- `generate_pdf_embeddings`를 먼저 실행합니다.
- embedding model을 바꾸면 전체 임베딩을 다시 생성합니다.

### 중복 또는 변경 결과가 예상과 다른 경우

- 중복 판정은 파일명이나 경로가 아니라 SHA-256, 파서 프로필과 청크 프로필을 함께 사용합니다.
- 같은 파일을 새 버전으로 강제 처리하려면 `force=true`를 사용합니다.
- `check_pdf_changes`는 원본 파일이 이동·삭제된 경우 `missing`으로 표시합니다.

## 테스트

단위 및 저장소 테스트:

```powershell
dotnet run --project .\mcp-pdf\tests\McpPdf.Tests.csproj -c Release
```

모의 Docling을 사용하는 MCP 종단간 스모크 테스트:

```powershell
.\tests\pdf-smoke.ps1
```

실제 Docling과 실제 PDF를 사용하는 종단간 테스트:

```powershell
.\tests\pdf-real-e2e.ps1 `
  -PdfPath 'D:\documents\manual.pdf' `
  -Profile balanced-ko `
  -ChunkProfile rag-default
```

이 테스트는 수집, 작업 polling, 페이지·목차·표·이미지·청크 조회, 검색, 검증, 렌더링, 재청킹, 중복, 변경 감지, 취소, JSONL 및 Parquet 내보내기를 확인합니다. 격리 데이터 디렉터리가 기본이며, 실제 운영 데이터에서 destructive 검사를 수행하지 않도록 주의해야 합니다.

## RAG 서버와 연결하는 방법

권장 경계는 다음과 같습니다.

```text
MCP-PDF
  PDF 수집 → 구조화 → 청킹 → 품질 검증 → 저장/내보내기

별도 RAG 서버
  질의 분석 → 검색/필터 → rerank → 프롬프트 구성 → LLM 답변
```

별도 RAG 서버는 상황에 맞게 다음 중 하나를 선택할 수 있습니다.

- JSONL 또는 Parquet를 일괄 적재
- PostgreSQL의 `mcp_pdf_chunks` 테이블 사용
- Qdrant collection 사용
- MCP-PDF의 `search_pdf_content`, `find_pdf_sources`, `get_pdf_chunk`, `read_pdf_pages`를 직접 호출

이 분리를 유지하면 PDF 처리 파이프라인과 질의·답변 정책을 독립적으로 변경하고 재검증할 수 있습니다.
