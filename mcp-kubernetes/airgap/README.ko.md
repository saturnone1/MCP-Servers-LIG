# mcp-kubernetes Air Gap 사용법

이 폴더는 `local/mcp-kubernetes:latest` 이미지를 air gap 환경으로 옮기기 위한 공간입니다. tar 파일은 Git에 커밋하지 않습니다.

## 인터넷 가능한 PC에서 추출

```powershell
.\scripts\export-airgap.ps1 -Servers mcp-kubernetes
```

생성 파일:

```text
mcp-kubernetes\airgap\local-mcp-kubernetes.tar
```

## Air Gap PC에서 로드

```powershell
docker load -i .\mcp-kubernetes\airgap\local-mcp-kubernetes.tar
```

## 실행

```powershell
.\mcp-kubernetes\airgap\run-docker-mcp.ps1 -Server mcp-kubernetes -Port 8087
```

연결 주소:

- HTTP: `http://localhost:8087/mcp`
- SSE: `http://localhost:8087/sse`

## Air Gap 참고

`kubectl`은 이미지 안에 포함됩니다. Air gap 내부 Kubernetes API 서버에 접근 가능한 kubeconfig가 필요합니다. 외부 cluster 주소나 image registry 접근은 air gap 네트워크 정책에 따릅니다.
