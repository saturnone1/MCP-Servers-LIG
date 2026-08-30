param(
    [Parameter(Mandatory)][string]$BundlePath,
    [string]$HostName = '192.168.0.11',
    [string]$UserName = 'saturnone1'
)
$ErrorActionPreference = 'Stop'
$bundle = (Resolve-Path $BundlePath).Path
if (-not (Test-Path (Join-Path $bundle 'SHA256SUMS'))) { throw '검증 가능한 MCP 번들이 아닙니다.' }
$leaf = Split-Path $bundle -Leaf
$remote = "/home/$UserName/mcp-airgap/$leaf"
ssh "$UserName@$HostName" "mkdir -p '$remote'"
if ($LASTEXITCODE) { throw '원격 디렉터리 생성 실패' }
scp -r "$bundle/." "$UserName@$HostName`:$remote/"
if ($LASTEXITCODE) { throw '파일 업로드 실패' }
Write-Host "Uploaded files only: $UserName@$HostName`:$remote"
