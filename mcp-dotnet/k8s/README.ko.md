# mcp-dotnet Kubernetes 배포

이 매니페스트는 `mcp-dotnet`을 Kubernetes의 `mcp-servers` namespace에 배포합니다.

## 적용

```powershell
kubectl apply -f .\mcp-dotnet\k8s\
```

## 접속 확인

```powershell
kubectl -n mcp-servers port-forward svc/mcp-dotnet 8084:8080
```

## 호환성

- `/workspace`는 PVC(`mcp-dotnet-workspace`)로 마운트됩니다.
- `sdk_info`, project discovery, 이미 restore된 프로젝트의 build/test는 cluster 내부에서 동작합니다.
- air gap cluster에서 `restore`, `add_package`를 쓰려면 NuGet cache 또는 내부 NuGet feed를 준비해야 합니다.
