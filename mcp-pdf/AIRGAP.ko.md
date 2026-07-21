# MCP-PDF 에어갭 배포 가이드

이 문서는 인터넷에 연결할 수 없는 Windows 에어갭 PC에서 LIG AI MCP의 MCP-PDF와 Docling Serve를 함께 설치하고 연결하는 절차를 설명합니다.

권장 방식은 다음과 같습니다.

- MCP-PDF는 LIG AI MCP Windows Setup으로 설치합니다.
- Docling은 공식 `docling-serve` 컨테이너를 Docker Desktop에서 실행합니다.
- Docling 이미지와 모델 캐시는 인터넷 연결이 가능한 준비 PC에서 완성하여 반입합니다.
- MCP-PDF와 Docling은 외부 네트워크에 노출하지 않고 `127.0.0.1`로만 연결합니다.

## 왜 이미지와 모델 캐시를 모두 반입해야 하는가

Docling 컨테이너 이미지는 실행 환경과 프로그램을 포함하지만, OCR·layout·table 분석 등에 사용하는 모델 artifact는 최초 변환 과정에서 캐시 디렉터리에 준비될 수 있습니다.

```text
Docling 컨테이너 이미지
  └─ Docling Serve, Python, 시스템 라이브러리

lig-docling-cache Docker volume
  └─ OCR, layout, table 등 실제 변환에 필요한 모델 캐시
```

따라서 `docker save`로 컨테이너 이미지만 옮기면 서버 health는 정상이어도 첫 PDF 처리에서 모델 다운로드를 시도하거나 실패할 수 있습니다. 온라인 준비 PC에서 사용할 프로필을 실제로 실행한 뒤 캐시 volume까지 별도로 archive해야 합니다.

## 목표 구성

```text
LLM/MCP Client
      │ MCP HTTP
      ▼
MCP-PDF (127.0.0.1:42199)
      │ Docling REST API
      ▼
Docling Serve (127.0.0.1:5001)
      │
      └─ lig-docling-cache Docker volume

로컬 저장소
%LOCALAPPDATA%\LIG AI MCP\pdf\mcp-pdf.db
```

두 포트 모두 localhost에만 바인딩하면 다른 장비에서 접근할 수 없습니다. Docling을 별도 내부 서버에 배치하는 구성은 이 문서 뒤쪽에서 설명합니다.

## 준비물

### 인터넷 연결 준비 PC

- Windows 10/11 또는 조직에서 승인한 Windows Server 환경
- Docker Desktop 또는 호환 Docker Engine
- 충분한 여유 공간
- 대표 디지털 PDF 한 개
- 텍스트 레이어가 없는 대표 스캔 PDF 한 개
- 승인된 반입 디렉터리 또는 이동식 매체

### 에어갭 대상 PC

- LIG AI MCP가 지원하는 64비트 Windows
- Docker Desktop 실행에 필요한 WSL 2 또는 Hyper-V 조건
- 조직 정책상 승인된 Docker Desktop 라이선스와 설치 권한
- 최소 수 GiB 이상의 컨테이너·모델 저장 공간
- CPU 실행 시 대형 문서를 처리할 수 있는 메모리와 시간

Docker Desktop을 설치할 수 없는 환경이라면 별도의 Linux Docling 서버를 내부망에 구성하거나 로컬 Docling CLI를 오프라인 설치해야 합니다. Python wheel과 모델 의존성이 많으므로 Windows 에어갭에서는 Docker 방식이 가장 재현성이 높습니다.

## 1. 온라인 준비 PC에서 배포 키트 디렉터리 만들기

아래 예시는 `D:\lig-mcp-pdf-airgap`을 사용합니다. 조직의 승인된 경로로 바꾸어도 됩니다.

```powershell
$kit = 'D:\lig-mcp-pdf-airgap'
New-Item -ItemType Directory -Path $kit -Force
```

키트의 최종 구조는 다음과 같이 구성합니다.

```text
lig-mcp-pdf-airgap\
├─ LIG-AI-MCP-Setup-<version>-win-x64.exe
├─ Docker-Desktop-Installer.exe           # 대상 PC에 없을 때
├─ docling-serve-v1.21.0.tar
├─ alpine-3.20.tar                        # cache archive/restore 보조 이미지
├─ lig-docling-cache.tgz
├─ test-digital.pdf                       # 반입이 허용되는 시험 문서
├─ test-scanned.pdf                       # 반입이 허용되는 시험 문서
└─ SHA256SUMS.txt
```

시험 PDF에 민감정보가 포함되지 않았는지 반드시 확인하십시오.

## 2. 필요한 Docker 이미지 받기

이 가이드는 검증된 버전을 고정하여 사용합니다.

```powershell
docker pull quay.io/docling-project/docling-serve:v1.21.0
docker pull alpine:3.20
```

이미지와 digest를 기록합니다.

```powershell
docker image inspect quay.io/docling-project/docling-serve:v1.21.0 `
  --format '{{json .RepoDigests}}'

docker image inspect alpine:3.20 `
  --format '{{json .RepoDigests}}'
```

버전을 변경할 경우 온라인 검증부터 다시 수행하고 키트 manifest에 새 버전과 digest를 기록해야 합니다.

## 3. Docling 모델 캐시 사전 준비

### 컨테이너와 cache volume 생성

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

기존에 같은 이름의 컨테이너가 있다면 먼저 그 컨테이너가 이 배포 준비용인지 확인하십시오. 다른 용도의 컨테이너를 임의로 삭제하지 마십시오.

### health 확인

Docling의 첫 시작과 모델 초기화에는 시간이 걸릴 수 있습니다.

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:5001/health' -TimeoutSec 30
docker logs --tail 100 lig-docling-serve
```

정상 결과는 다음과 같습니다.

```json
{"status":"ok"}
```

### 실제 변환으로 모델 준비

MCP-PDF에서 실제 사용할 프로필을 모두 대표하도록 최소 두 번 변환합니다.

1. 텍스트 레이어, 표, 그림이 포함된 디지털 PDF를 `accurate-ko`에 해당하는 옵션으로 변환
2. 텍스트 레이어가 없는 스캔 PDF를 강제 OCR 옵션으로 변환

MCP-PDF 자체를 연결해 `start_pdf_ingest`로 수행하는 것이 가장 확실합니다. 아직 MCP-PDF가 없다면 Docling REST API를 직접 호출할 수도 있습니다.

디지털 PDF 예시:

```powershell
$digitalPdf = 'D:\airgap-test\test-digital.pdf'

curl.exe --fail --show-error `
  -X POST 'http://127.0.0.1:5001/v1/convert/file' `
  -F "files=@$digitalPdf;type=application/pdf" `
  -F 'from_formats=pdf' `
  -F 'to_formats=json' `
  -F 'do_ocr=true' `
  -F 'force_ocr=false' `
  -F 'ocr_lang=kor' `
  -F 'ocr_lang=eng' `
  -F 'do_table_structure=true' `
  -F 'table_mode=accurate' `
  -F 'image_export_mode=embedded' `
  -F 'generate_picture_images=true' `
  -F 'do_code_enrichment=true' `
  -F 'do_formula_enrichment=true' `
  -o (Join-Path $kit 'prewarm-digital-result.json')
```

스캔 PDF 예시:

```powershell
$scannedPdf = 'D:\airgap-test\test-scanned.pdf'

curl.exe --fail --show-error `
  -X POST 'http://127.0.0.1:5001/v1/convert/file' `
  -F "files=@$scannedPdf;type=application/pdf" `
  -F 'from_formats=pdf' `
  -F 'to_formats=json' `
  -F 'do_ocr=true' `
  -F 'force_ocr=true' `
  -F 'ocr_lang=kor' `
  -F 'ocr_lang=eng' `
  -F 'do_table_structure=true' `
  -F 'table_mode=accurate' `
  -F 'image_export_mode=embedded' `
  -F 'generate_picture_images=true' `
  -o (Join-Path $kit 'prewarm-scanned-result.json')
```

두 명령 모두 HTTP 오류 없이 JSON 결과를 만들었는지 확인합니다. 특정 조직 문서에서 코드·수식·특수 언어 모델이 필요하다면 그 문서 유형도 온라인 준비 단계에서 추가로 실행합니다.

## 4. 컨테이너 이미지와 모델 캐시 내보내기

### Docker 이미지 저장

```powershell
docker image save `
  --output (Join-Path $kit 'docling-serve-v1.21.0.tar') `
  quay.io/docling-project/docling-serve:v1.21.0

docker image save `
  --output (Join-Path $kit 'alpine-3.20.tar') `
  alpine:3.20
```

### 모델 캐시 archive

캐시가 갱신되는 도중 archive하지 않도록 Docling을 잠시 중지합니다.

```powershell
docker stop lig-docling-serve

docker run --rm `
  --mount 'type=volume,source=lig-docling-cache,target=/cache,readonly' `
  --mount "type=bind,source=$kit,target=/backup" `
  alpine:3.20 `
  sh -c 'cd /cache && tar czf /backup/lig-docling-cache.tgz .'

docker start lig-docling-serve
```

archive 크기가 지나치게 작다면 캐시가 제대로 준비되지 않았을 수 있습니다.

```powershell
Get-Item (Join-Path $kit 'lig-docling-cache.tgz') |
  Select-Object FullName, Length, LastWriteTime
```

## 5. LIG Setup과 내장 Poppler 준비

### LIG AI MCP Setup

빌드된 단일 Setup 파일을 키트에 복사합니다.

```powershell
Copy-Item `
  '.\installer\output\LIG-AI-MCP-Setup-1.0.12-win-x64.exe' `
  $kit
```

버전이 올라가면 실제 생성된 파일명을 사용하십시오. 에어갭 대상 PC에서 설치는 현장 사용자가 직접 진행합니다.

### Setup에 포함되는 Poppler

Poppler는 페이지 PNG 렌더링에만 필요하며 PDF 파싱과 청킹에는 필수가 아닙니다. LIG Setup에는 portable Poppler 전체 배포본이 선택 Feature로 내장되므로 별도 `poppler.zip`을 에어갭 키트에 넣을 필요가 없습니다.

Setup을 직접 빌드하는 온라인 준비 PC에는 `Library\bin\pdftoppm.exe`가 포함된 검증된 Poppler 배포 루트가 있어야 합니다. 빌드 스크립트는 winget 설치를 자동 탐색하거나 `-PopplerRoot` 인수를 사용합니다.

```powershell
winget install --id oschwartz10612.Poppler --exact

.\scripts\build-installer.ps1 `
  -PopplerRoot 'D:\dependencies\poppler-25.07.0'
```

빌드는 Poppler 전체 폴더를 Setup payload에 넣고 `pdftoppm -v`, SHA-256과 payload 크기를 확인합니다. 사용한 Windows 배포본의 전체 라이선스·제3자 고지 및 소스 제공 의무는 정식 반입 전에 검토해야 합니다.

### Docker Desktop 오프라인 설치 파일

대상 PC에 Docker Desktop이 없다면 공식 오프라인 설치 파일도 키트에 포함합니다. 조직의 라이선스·보안 정책, WSL 2/Hyper-V 사용 가능 여부와 설치 후 재부팅 필요성을 사전에 확인하십시오.

## 6. SHA-256 manifest 만들기

결과 JSON과 시험 PDF를 배포 키트에 포함할지는 조직의 반입 정책에 따라 결정합니다. 최종 반입 대상 파일만 남긴 뒤 manifest를 생성합니다.

```powershell
Get-ChildItem -LiteralPath $kit -File |
  Where-Object Name -ne 'SHA256SUMS.txt' |
  Sort-Object Name |
  ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
  } | Set-Content -LiteralPath (Join-Path $kit 'SHA256SUMS.txt') -Encoding ASCII
```

manifest 자체는 조직의 승인된 방식으로 서명하거나 별도 신뢰 채널로 전달하는 것을 권장합니다.

## 7. 온라인 상태를 차단하고 최종 사전 검증

가장 중요한 단계입니다. 준비 PC의 외부 네트워크를 차단하거나 조직에서 허용한 격리 시험 환경으로 옮긴 뒤 다음을 확인합니다.

1. Docling 컨테이너를 재시작합니다.
2. `/health`가 정상인지 확인합니다.
3. 디지털 PDF를 다시 변환합니다.
4. 스캔 PDF를 강제 OCR로 다시 변환합니다.
5. 로그에 모델 다운로드 실패나 외부 URL 연결 시도가 없는지 확인합니다.

이 단계가 통과하지 않으면 모델 캐시가 완전하다고 볼 수 없습니다. 누락된 문서 유형이나 모델을 온라인 상태에서 다시 준비한 뒤 cache archive와 SHA-256 manifest를 재생성합니다.

## 8. 에어갭 PC에서 반입 파일 검증

반입 매체를 로컬 승인 경로로 복사한 뒤 해시를 확인합니다. 아래 예시는 `E:\lig-mcp-pdf-airgap`입니다.

```powershell
$kit = 'E:\lig-mcp-pdf-airgap'
$failed = @()

Get-Content -LiteralPath (Join-Path $kit 'SHA256SUMS.txt') | ForEach-Object {
  if ($_ -notmatch '^([0-9a-fA-F]{64})\s{2}(.+)$') { return }
  $expected = $matches[1].ToLowerInvariant()
  $name = $matches[2]
  $path = Join-Path $kit $name
  if (-not (Test-Path -LiteralPath $path)) {
    $failed += "missing: $name"
    return
  }
  $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actual -ne $expected) { $failed += "hash mismatch: $name" }
}

if ($failed.Count -gt 0) {
  $failed
  throw '에어갭 반입 파일 검증에 실패했습니다.'
}

'모든 반입 파일의 SHA-256이 일치합니다.'
```

하나라도 일치하지 않으면 설치하지 말고 반입 절차부터 확인합니다.

## 9. Docker Desktop과 LIG AI MCP 설치

1. Docker Desktop이 없다면 승인된 오프라인 설치 파일로 설치합니다.
2. 필요하면 재부팅하고 Docker Desktop이 정상 실행되는지 확인합니다.
3. LIG AI MCP Setup을 현장 사용자가 실행합니다.
4. 설치 완료 후 아직 MCP-PDF를 시작하지 않아도 됩니다.

Docker 확인:

```powershell
docker version
docker info
```

이 문서는 LIG Setup을 자동 실행하지 않습니다. 설치·업그레이드·제거는 현장 정책과 사용자 승인에 따라 수행하십시오.

## 10. Docker 이미지 불러오기

```powershell
$kit = 'E:\lig-mcp-pdf-airgap'

docker image load --input (Join-Path $kit 'docling-serve-v1.21.0.tar')
docker image load --input (Join-Path $kit 'alpine-3.20.tar')

docker image inspect quay.io/docling-project/docling-serve:v1.21.0 `
  --format '{{json .RepoDigests}}'
```

`docker pull`은 실행하지 않습니다. 에어갭에서는 항상 반입한 고정 이미지로 시작해야 합니다.

## 11. 모델 캐시 복원

새 cache volume을 만들고 archive를 복원합니다.

```powershell
docker volume create lig-docling-cache

docker run --rm `
  --mount 'type=volume,source=lig-docling-cache,target=/cache' `
  --mount "type=bind,source=$kit,target=/backup,readonly" `
  alpine:3.20 `
  sh -c 'cd /cache && tar xzf /backup/lig-docling-cache.tgz'
```

복원된 파일이 존재하는지 확인합니다.

```powershell
docker run --rm `
  --mount 'type=volume,source=lig-docling-cache,target=/cache,readonly' `
  alpine:3.20 `
  sh -c 'du -sh /cache && find /cache -type f | head -n 20'
```

## 12. 에어갭 PC에서 Docling Serve 실행

```powershell
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

확인:

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:5001/health' -TimeoutSec 30
docker ps --filter 'name=lig-docling-serve'
docker logs --tail 100 lig-docling-serve
```

`127.0.0.1:5001:5001`을 유지해야 Docling 포트가 외부 인터페이스에 공개되지 않습니다.

### PC 사양별 성능 설정

Docling 모델은 CPU에서도 실행할 수 있으며 GPU는 필수가 아닙니다. 현재 노트북 검증에서는 CPU 4 thread, worker 1로 90페이지 기술문서를 약 9분에 처리했고 메모리는 대략 2.4~3.5GiB를 사용했습니다. 스캔 OCR과 복잡한 문서는 더 많은 시간과 메모리를 사용할 수 있습니다.

일반 노트북의 보수적인 시작 설정:

```powershell
-e DOCLING_DEVICE=cpu `
-e DOCLING_NUM_THREADS=4 `
-e DOCLING_SERVE_ENG_LOC_NUM_WORKERS=1
```

코어가 적거나 다른 업무와 함께 사용하는 PC라면 thread를 2로 낮출 수 있습니다. 코어가 많은 PC에서는 물리 코어 수를 기준으로 thread를 늘리되, worker 수와 곱한 총 CPU 사용량을 확인합니다.

메모리가 충분한 CPU 워크스테이션은 worker를 2부터 시험할 수 있습니다.

```powershell
-e DOCLING_DEVICE=cpu `
-e DOCLING_NUM_THREADS=8 `
-e DOCLING_SERVE_ENG_LOC_NUM_WORKERS=2
```

worker를 늘리면 모델과 중간 데이터 때문에 메모리 사용량이 증가할 수 있습니다. 운영에 적용하기 전에 가장 큰 PDF 두 개를 동시에 처리해 메모리 부족과 처리시간을 측정하십시오.

NVIDIA GPU PC 예시:

```powershell
docker run -d `
  --name lig-docling-serve `
  --restart unless-stopped `
  --gpus all `
  -p 127.0.0.1:5001:5001 `
  -v lig-docling-cache:/opt/app-root/src/.cache `
  -e DOCLING_DEVICE=cuda `
  -e DOCLING_NUM_THREADS=4 `
  -e DOCLING_SERVE_ENG_LOC_NUM_WORKERS=1 `
  -e DOCLING_SERVE_ENABLE_UI=0 `
  quay.io/docling-project/docling-serve:v1.21.0
```

GPU 구성에는 호환 NVIDIA driver와 Docker GPU passthrough가 필요합니다. 다음 명령이 컨테이너에서 GPU를 보여주는지 먼저 확인합니다.

```powershell
docker run --rm --gpus all `
  quay.io/docling-project/docling-serve:v1.21.0 `
  python -c "import torch; print(torch.cuda.is_available()); print(torch.cuda.get_device_name(0))"
```

`True`와 GPU 이름이 나오지 않으면 `DOCLING_DEVICE=cuda`로 운영하지 마십시오.

### 조정 가능한 주요 환경 변수

| 환경 변수 | 용도 |
|---|---|
| `DOCLING_DEVICE` | `cpu`, `auto`, `cuda`, `cuda:N`, `mps`, `xpu` 장치 선택 |
| `DOCLING_NUM_THREADS` | 추론 작업당 CPU thread 수 |
| `OMP_NUM_THREADS` | `DOCLING_NUM_THREADS`가 없을 때의 대체 thread 값 |
| `DOCLING_SERVE_ENG_LOC_NUM_WORKERS` | 로컬 동시 변환 worker 수 |
| `DOCLING_SERVE_ENG_LOC_SHARE_MODELS` | 복수 worker 모델 공유 설정 |
| `DOCLING_SERVE_LOAD_MODELS_AT_BOOT` | 시작 시 모델 적재 및 에어갭 누락 조기 확인 |
| `DOCLING_SERVE_OPTIONS_CACHE_SIZE` | 변환 옵션별 pipeline cache 수 |
| `DOCLING_SERVE_OCR_BATCH_SIZE` | OCR batch 크기 |
| `DOCLING_SERVE_LAYOUT_BATCH_SIZE` | Layout batch 크기 |
| `DOCLING_SERVE_TABLE_BATCH_SIZE` | Table batch 크기 |
| `DOCLING_SERVE_QUEUE_MAX_SIZE` | 대기 작업 queue 상한 |
| `DOCLING_SERVE_MAX_NUM_PAGES` | 요청당 최대 페이지 수 |
| `DOCLING_SERVE_MAX_FILE_SIZE` | 요청당 최대 파일 크기 |
| `DOCLING_SERVE_MAX_DOCUMENT_TIMEOUT` | 문서 처리 제한시간의 허용 상한 |

일반적인 단일 PC 운영에 필요한 조정 항목은 충분합니다. 복수 서버와 Redis/Ray를 사용하는 분산 처리 설정도 Docling Serve에 있지만, 노트북이나 단일 에어갭 PC에서는 복잡성만 늘어나므로 사용하지 않는 것을 권장합니다.

환경 변수는 컨테이너 생성 시 고정됩니다. 변경하려면 cache volume을 보존한 상태로 컨테이너만 다시 만들어야 합니다.

```powershell
docker stop lig-docling-serve
docker rm lig-docling-serve

# 새로운 -e 설정으로 앞의 docker run 명령을 다시 실행합니다.
```

`lig-docling-cache` volume을 삭제하지 않으면 다운로드한 범용 모델은 유지됩니다. `docker volume rm lig-docling-cache`는 실행하지 마십시오.

Docker CPU·메모리 상한은 환경 변수가 아니라 `docker run`의 `--cpus`, `--memory` 옵션 또는 Docker Desktop Resource 설정으로 관리합니다.

MCP-PDF 측의 `PDF_MAX_CONCURRENT_JOBS`도 별도 조정 대상입니다. 저사양 PC에서는 다음처럼 사용자별 환경에 설정하고 MCP-PDF를 재시작합니다.

```powershell
$lig = 'C:\Program Files\LIG AI MCP\LIG-AI-MCP.cmd'
& $lig set-env mcp-pdf PDF_MAX_CONCURRENT_JOBS 1
& $lig restart mcp-pdf
```

Docling worker가 1인데 MCP-PDF 작업만 많이 늘리면 Docling queue 대기만 길어질 수 있습니다. 두 동시성 값을 함께 부하 시험해 결정하십시오.

## 13. 내장 Poppler 선택과 MCP-PDF 설정

LIG Setup을 대화형으로 실행하면 다음 질문이 설치 진행 창보다 먼저 표시됩니다.

```text
PDF 페이지 렌더링 도구 Poppler를 함께 설치하시겠습니까?
[예] [아니요] [취소]
```

기본값이자 권장값인 `예`를 선택하면 다음 위치에 설치됩니다.

```text
C:\Program Files\LIG AI MCP\dependencies\poppler\Library\bin\pdftoppm.exe
```

MCP-PDF는 이 경로를 자동으로 발견하므로 `PDF_RENDER_COMMAND`를 수동 설정할 필요가 없습니다. `아니요`를 선택하면 페이지 렌더링만 제외되며 Docling 수집·청킹·검색은 그대로 사용할 수 있습니다.

조용한 자동 설치에서는 다음 옵션을 사용할 수 있습니다.

```text
LIG-AI-MCP-Setup-<version>-win-x64.exe --quiet --with-poppler
LIG-AI-MCP-Setup-<version>-win-x64.exe --quiet --without-poppler
```

`--quiet`만 지정하면 Poppler를 포함합니다.

설치 후 내장 실행 파일을 확인합니다.

```powershell
$pdftoppm = 'C:\Program Files\LIG AI MCP\dependencies\poppler\Library\bin\pdftoppm.exe'
& $pdftoppm -v
```

LIG Manager의 사용자별 MCP-PDF 환경에는 Docling 연결값을 기록합니다.

```powershell
$lig = 'C:\Program Files\LIG AI MCP\LIG-AI-MCP.cmd'

& $lig set-env mcp-pdf DOCLING_MODE remote
& $lig set-env mcp-pdf DOCLING_SERVICE_URL http://127.0.0.1:5001
& $lig set-env mcp-pdf DOCLING_USE_ASYNC true
```

사용자별 환경 파일은 다음 경로에 저장됩니다.

```text
%LOCALAPPDATA%\LIG AI MCP\.mcp-manager\env\mcp-pdf.env
```

직접 확인하거나 수정하려면 다음 명령을 사용할 수 있습니다.

```powershell
& $lig env mcp-pdf
```

환경 파일을 변경한 뒤에는 MCP-PDF를 재시작해야 합니다.

```powershell
& $lig restart mcp-pdf
```

조직에서 별도로 관리하는 Poppler를 사용해야 한다면 그때만 `PDF_RENDER_COMMAND`에 절대 경로를 지정합니다. 이 명시적 경로는 내장 Poppler보다 우선합니다.

## 14. 연결 검증

### Docling

```powershell
Invoke-RestMethod 'http://127.0.0.1:5001/health'
```

### MCP-PDF

```powershell
Invoke-RestMethod 'http://127.0.0.1:42199/healthz' |
  ConvertTo-Json -Depth 8
```

예상되는 핵심 값:

```json
{
  "status": "healthy",
  "server": "mcp-pdf",
  "runtime": {
    "parser": "docling-serve",
    "doclingMode": "remote",
    "doclingAsync": true
  }
}
```

MCP 클라이언트에서 `config` 도구를 호출해 `parserHealth.available=true`인지도 확인합니다.

## 15. 에어갭 실제 PDF 수집 검증

health 확인만으로는 모델 캐시가 완전한지 알 수 없습니다. 반입이 허용된 시험 PDF로 실제 수집까지 진행합니다.

권장 순서:

1. `config`
2. `start_pdf_ingest` (`balanced-ko`, `rag-default`)
3. `get_pdf_job_status` polling
4. `get_pdf_job_events`
5. `validate_pdf_dataset`
6. `read_pdf_pages`로 첫·중간·마지막 페이지 확인
7. 실제 본문 키워드 3개로 `search_pdf_content`
8. `get_pdf_tables`, `get_pdf_images`
9. Setup에서 Poppler를 선택했다면 `render_pdf_pages`
10. `export_pdf_dataset`으로 JSONL과 Parquet 생성

스캔 문서도 사용할 예정이라면 별도 스캔 PDF를 `scanned-ko`로 수집하여 `ocrPages`가 1 이상인지 확인합니다.

## 16. 완전한 오프라인 동작 확인

다음 조건에서 실제 변환을 다시 수행합니다.

- Wi-Fi와 유선 외부망이 차단됨
- 프록시가 설정되지 않음
- 사내 인터넷 gateway에 접근할 수 없음
- Docker가 반입 이미지와 로컬 cache volume만 사용함

Docling 로그에 다음과 같은 내용이 없어야 합니다.

- DNS resolution 실패
- Hugging Face 또는 외부 저장소 다운로드 시도
- model/artifact not found
- connection timeout 후 parser failure

```powershell
docker logs --since 30m lig-docling-serve
```

로그와 MCP-PDF 작업 이벤트를 함께 보관하면 에어갭 승인 및 장애 분석에 도움이 됩니다.

## 17. 내부망의 별도 Docling 서버 사용

MCP-PDF와 Docling을 같은 PC에서 실행할 필요는 없습니다. GPU 서버나 공용 내부 문서 처리 서버가 있다면 Docling을 해당 서버에 배치할 수 있습니다.

```text
에어갭 사용자 PC                      내부 Docling 서버
MCP-PDF  ───── 사내 허용 포트 ─────▶  Docling Serve
```

MCP-PDF 설정:

```powershell
& $lig set-env mcp-pdf DOCLING_SERVICE_URL http://10.10.20.30:5001
& $lig restart mcp-pdf
```

이 경우 다음을 별도로 설계해야 합니다.

- 서버 방화벽과 클라이언트 허용 목록
- PDF에 포함된 민감정보의 내부망 전송 정책
- TLS 또는 reverse proxy
- API key와 `DOCLING_SERVICE_API_KEY`
- 동시 사용자 수와 worker 수
- 서버의 모델 cache와 백업

localhost 방식보다 공격 표면과 운영 복잡도가 커지므로 조직 정책이 요구하는 경우에만 사용하십시오.

## 18. 업데이트 절차

에어갭 환경에서 즉석 업데이트하지 않습니다. 새 LIG 버전 또는 Docling 버전마다 온라인 준비 환경에서 새 키트를 만듭니다.

1. 새 버전을 별도 태그로 pull/build합니다.
2. 대표 디지털·스캔 PDF 회귀 검증을 수행합니다.
3. 새 모델 캐시를 준비합니다.
4. 이미지와 캐시를 다시 export합니다.
5. SHA-256 manifest와 변경 기록을 작성합니다.
6. 승인된 반입 절차를 거칩니다.
7. 기존 DB와 cache의 백업·복구 계획을 세운 뒤 업데이트합니다.

운영 중인 `mcp-pdf.db`와 `%LOCALAPPDATA%\LIG AI MCP\pdf` 데이터는 설치 프로그램과 별도로 백업하십시오.

SQLite DB를 복사할 때는 MCP-PDF를 중지하거나 SQLite backup API를 사용해야 합니다. 실행 중 DB 파일만 복사하면 WAL에 남은 변경이 누락될 수 있습니다.

## 19. 문제 해결

### Docling health는 정상이지만 수집이 실패함

- 모델 캐시가 완전히 반입됐는지 확인합니다.
- `docker logs lig-docling-serve`에서 외부 다운로드 시도를 찾습니다.
- MCP-PDF의 `get_pdf_job_events`에서 HTTP 상태와 parser 오류를 확인합니다.
- 온라인 준비 시 사용하지 않은 프로필이나 enrichment가 요청됐는지 확인합니다.

### `127.0.0.1:5001`에 연결할 수 없음

```powershell
docker ps -a --filter 'name=lig-docling-serve'
docker port lig-docling-serve 5001/tcp
docker logs --tail 200 lig-docling-serve
```

- Docker Desktop이 실행 중인지 확인합니다.
- 컨테이너 상태가 `Exited`이면 로그의 시작 오류를 확인합니다.
- 동일 포트를 다른 프로세스가 사용 중인지 확인합니다.
- 포트가 `127.0.0.1:5001`로 게시됐는지 확인합니다.

### 모델 캐시 권한 오류

cache archive를 Windows에서 풀어 다시 복사하지 말고, 안내된 Alpine 컨테이너로 Docker volume에 직접 복원합니다. tar가 Linux 파일 권한과 디렉터리 구조를 보존합니다.

### Poppler 실행 시 DLL 오류

Setup에서 Poppler를 선택했는지와 `C:\Program Files\LIG AI MCP\dependencies\poppler\Library\bin\pdftoppm.exe`가 존재하는지 확인합니다. 설치 payload에는 관련 DLL과 데이터가 포함된 전체 배포본이 들어가야 하므로, 직접 빌드한 Setup이라면 `-PopplerRoot`가 올바른 배포 루트를 가리켰는지도 확인합니다.

### 처리 속도가 너무 느림

CPU 전용 Docling은 대형·스캔 PDF에서 오래 걸릴 수 있습니다.

- worker 1로 먼저 안정성을 확인합니다.
- Docker Desktop 메모리를 늘립니다.
- 동시 MCP-PDF 작업 수를 낮춥니다.
- 내부 GPU Docling 서버 사용을 검토합니다.
- 단, 성능 설정을 바꾸면 대표 문서 회귀 검증을 다시 수행합니다.

## 20. 인수인계 체크리스트

- [ ] LIG Setup 파일명, 버전, 크기와 SHA-256 기록
- [ ] Docling 이미지 태그와 digest 기록
- [ ] cache archive 크기와 SHA-256 기록
- [ ] 보조 이미지 태그와 digest 기록
- [ ] Docker Desktop 버전 및 조직 라이선스 확인
- [ ] Setup 내장 Poppler 버전, `pdftoppm.exe` SHA-256 및 라이선스 고지 기록
- [ ] 디지털 PDF 사전 준비 완료
- [ ] 스캔 PDF 강제 OCR 사전 준비 완료
- [ ] 네트워크 차단 상태 사전 검증 완료
- [ ] 에어갭 PC에서 전체 파일 해시 일치
- [ ] Docling `/health` 통과
- [ ] MCP-PDF `/healthz`와 `config` 통과
- [ ] 실제 PDF 수집 완료
- [ ] 페이지·표·검색·렌더링 검증 완료
- [ ] JSONL·Parquet 내보내기 검증 완료
- [ ] 로그·DB·모델 cache 백업 위치 문서화

## 관련 문서

- [MCP-PDF 전체 설명과 사용법](README.md)
- [환경 변수 예시](config/pdf.env.example)
- [파서·청크 프로필 override](config/profiles.json)
- [실제 PDF 종단간 테스트](../tests/pdf-real-e2e.ps1)
