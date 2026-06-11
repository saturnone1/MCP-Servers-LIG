param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\vendor\official'),
    [string]$Repository = 'matlab/matlab-mcp-core-server',
    [string]$AssetPattern = 'win64|windows-x64',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Get-LatestRelease([string]$Repo) {
    $headers = @{ 'User-Agent' = 'mcp-matlab-airgap-prep' }
    Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$release = $null
try {
    $release = Get-LatestRelease $Repository
}
catch {
    if ($Repository -eq 'matlab/matlab-mcp-core-server') {
        $Repository = 'matlab/matlab-mcp-server'
        $release = Get-LatestRelease $Repository
    }
    else {
        throw
    }
}

$asset = @($release.assets) |
    Where-Object { $_.name -match $AssetPattern -and ($_.name -like '*.exe' -or $_.name -match 'windows|win64') } |
    Select-Object -First 1

if ($null -eq $asset) {
    throw "No release asset matched '$AssetPattern' in $Repository release $($release.tag_name)."
}

$target = Join-Path $OutputDirectory $asset.name
if ((Test-Path -LiteralPath $target) -and -not $Force) {
    Write-Host "Already exists: $target"
    Write-Host "Use -Force to download again."
    exit 0
}

Write-Host "Downloading $($asset.browser_download_url)"
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $target
Write-Host "Downloaded official MATLAB MCP server to $target"
Write-Host "publish-win.ps1 will include files from $OutputDirectory in the air-gap Windows package."
