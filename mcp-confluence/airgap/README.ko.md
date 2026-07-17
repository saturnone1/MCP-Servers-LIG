# mcp-confluence Air Gap Export

```powershell
docker build -t local/mcp-confluence .
New-Item -ItemType Directory -Force airgap | Out-Null
docker save -o airgap/local-mcp-confluence.tar local/mcp-confluence:latest
```

대상 장비에서:

```powershell
docker load -i airgap/local-mcp-confluence.tar
docker run --rm -p 127.0.0.1:42198:8080 `
  -e "CONFLUENCE_BASE_URL=https://confluence.example.local" `
  -e "CONFLUENCE_PAT=<personal-access-token>" `
  local/mcp-confluence
```

