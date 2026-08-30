# LIG MCP 폐쇄망 번들

Kubernetes에 배포할 MCP 4종의 이미지와 Helm 차트를 폐쇄망 반입용으로 묶습니다. Mattermost는 포함하지 않습니다.

| 서버 | 고정 버전 | 원본 |
| --- | --- | --- |
| Kubernetes | v0.0.66 | `quay.io/containers/kubernetes_mcp_server` |
| Grafana | 1.3.0 | `docker.io/grafana/mcp-grafana` |
| Harbor | 저장소 소스 | `../mcp-harbor` |
| XWiki | v20260810-3 | `docker.io/lnikonl/xwiki-mcp` |

## 1. 번들 생성

Docker가 실행 중인 인터넷 연결 PC에서 실행합니다.

```powershell
.\lig-mcp-airgap\New-LigMcpAirgapBundle.ps1
```

결과는 `artifacts/lig-mcp-airgap/<날짜-버전>/`에 생성됩니다. 이 단계는 SSH나 `192.168.0.11`에 접근하지 않습니다.

## 2. 이동 서버 업로드

번들 생성과 검증을 마친 뒤 명시적으로 실행합니다.

```powershell
.\lig-mcp-airgap\Copy-LigMcpBundleToTransferServer.ps1 -BundlePath <번들경로>
```

이 스크립트는 파일만 `/home/saturnone1/mcp-airgap/`에 복사합니다. 원격 이미지 import, 컨테이너 실행, `kubectl`, Helm을 수행하지 않습니다.

## 3. 폐쇄망 설치

압축을 풀고 `SHA256SUMS`를 검증한 다음 이미지를 폐쇄망 Harbor에 push합니다. `helm/values-airgap.example.yaml`의 주소와 Secret을 실제 값으로 바꾸고 설치합니다.

```powershell
.\Push-LigMcpImages.ps1 -Registry harbor.example.local/lig-mcp
helm upgrade --install lig-mcp .\helm -n lig-mcp --create-namespace -f .\helm\values-airgap.example.yaml
```

MCP 엔드포인트는 각 Service의 `/mcp`입니다. 인증 토큰과 제품 자격 증명은 이미지나 values에 넣지 말고 `secret.example.yaml`처럼 서비스별 Secret으로 분리해 주입합니다.

이 번들은 운영 제어를 위해 Kubernetes MCP에 `cluster-admin`을 부여하고 Harbor 쓰기 및 XWiki 삭제 도구를 활성화합니다. MCP 호출자 접근은 반드시 내부 Gateway/NetworkPolicy와 인증 계층으로 제한하십시오.

기본 NetworkPolicy는 `lig-mcp-access=true` 라벨이 붙은 namespace에서만 MCP 포트 접근을 허용합니다. MCP client가 있는 namespace에 명시적으로 라벨을 붙입니다.

```powershell
kubectl label namespace <client-namespace> lig-mcp-access=true
```
