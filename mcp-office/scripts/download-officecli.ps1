param(
    [string]$Output = (Join-Path $PSScriptRoot '..\vendor\officecli'),
    [string]$Version = 'latest',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Platform = 'win-x64'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Output | Out-Null

if ($Version -eq 'latest') {
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/iOfficeAI/OfficeCLI/releases/latest'
}
else {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/iOfficeAI/OfficeCLI/releases/tags/$Version"
}

$assetName = "officecli-$Platform.exe"
$asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
if (-not $asset) {
    throw "Could not find OfficeCLI asset '$assetName' in release $($release.tag_name)."
}

$destination = Join-Path $Output 'officecli.exe'
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $destination

$shaPath = Join-Path $Output 'officecli.sha256'
if ($asset.digest -and $asset.digest.StartsWith('sha256:')) {
    $expected = $asset.digest.Substring('sha256:'.Length)
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $destination).Hash.ToLowerInvariant()
    if ($actual -ne $expected.ToLowerInvariant()) {
        throw "SHA256 mismatch for $destination. Expected $expected, got $actual."
    }
    Set-Content -LiteralPath $shaPath -Value "$actual  officecli.exe" -Encoding ASCII
}

@"
OfficeCLI $($release.tag_name)
Source: $($asset.browser_download_url)
Downloaded: $(Get-Date -Format o)
"@ | Set-Content -LiteralPath (Join-Path $Output 'README.txt') -Encoding UTF8

Write-Host "Downloaded $assetName from $($release.tag_name) to $destination"
