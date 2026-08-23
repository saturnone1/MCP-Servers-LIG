param(
    [string]$Output = (Join-Path $PSScriptRoot '..\vendor\plantuml'),
    [string]$Version = 'latest',
    # PlantUML ships the same engine under several licenses. The bundle is redistributed
    # inside a commercial installer, so a permissive edition is the default; 'gpl' is the
    # upstream plantuml.jar and pulls the whole product into GPL obligations.
    [ValidateSet('mit', 'asl', 'bsd', 'epl', 'lgpl', 'mit-light', 'gpl')]
    [string]$Edition = 'mit'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Output | Out-Null

if ($Version -eq 'latest') {
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/plantuml/plantuml/releases/latest'
}
else {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/plantuml/plantuml/releases/tags/$Version"
}

if ($Edition -eq 'gpl') {
    $pattern = '^plantuml\.jar$'
}
else {
    $pattern = "^plantuml-$([regex]::Escape($Edition))-[0-9][0-9.]*\.jar$"
}

$asset = $release.assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
if (-not $asset) {
    throw "Could not find a PlantUML '$Edition' jar in release $($release.tag_name)."
}

$destination = Join-Path $Output 'plantuml.jar'
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $destination

$shaPath = Join-Path $Output 'plantuml.sha256'
if ($asset.digest -and $asset.digest.StartsWith('sha256:')) {
    $expected = $asset.digest.Substring('sha256:'.Length)
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $destination).Hash.ToLowerInvariant()
    if ($actual -ne $expected.ToLowerInvariant()) {
        throw "SHA256 mismatch for $destination. Expected $expected, got $actual."
    }
    Set-Content -LiteralPath $shaPath -Value "$actual  plantuml.jar" -Encoding ASCII
}

@"
PlantUML $($release.tag_name) ($Edition edition)
Asset: $($asset.name)
Source: $($asset.browser_download_url)
Downloaded: $(Get-Date -Format o)

This jar still needs a Java runtime on the target machine. The bundle does not ship one.
"@ | Set-Content -LiteralPath (Join-Path $Output 'README.txt') -Encoding UTF8

Write-Host "Downloaded $($asset.name) from $($release.tag_name) to $destination"
