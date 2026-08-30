param([string]$Version = (Get-Date -Format 'yyyy-MM-dd'))
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$definition = Get-Content (Join-Path $PSScriptRoot 'images.json') -Raw | ConvertFrom-Json
$artifactRoot = Join-Path $root 'artifacts/lig-mcp-airgap'
$output = Join-Path $artifactRoot $Version
if (-not ([IO.Path]::GetFullPath($output)).StartsWith(([IO.Path]::GetFullPath($artifactRoot) + [IO.Path]::DirectorySeparatorChar))) {
    throw 'Invalid bundle output path.'
}
if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
$imageDir = Join-Path $output 'images'
New-Item -ItemType Directory -Force $imageDir | Out-Null

foreach ($item in $definition.images) {
    if ($item.buildContext) {
        docker build --pull -t $item.source (Join-Path $root $item.buildContext)
    } else {
        docker pull --platform linux/amd64 $item.source
    }
    if ($LASTEXITCODE) { throw "Image preparation failed: $($item.source)" }
    $archive = Join-Path $imageDir "$($item.name).tar"
    docker save -o $archive $item.source
    if ($LASTEXITCODE) { throw "Image export failed: $($item.source)" }
}

Copy-Item (Join-Path $PSScriptRoot 'helm') (Join-Path $output 'helm') -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'Push-LigMcpImages.ps1') $output -Force
Copy-Item (Join-Path $PSScriptRoot 'README.ko.md') $output -Force
Copy-Item (Join-Path $PSScriptRoot 'images.json') $output -Force

$manifest = foreach ($item in $definition.images) {
    $inspect = docker image inspect $item.source | ConvertFrom-Json
    [ordered]@{ name=$item.name; source=$item.source; target=$item.target; imageId=$inspect[0].Id; repoDigests=$inspect[0].RepoDigests }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'manifest.json') -Encoding utf8
Get-ChildItem $output -File -Recurse | Where-Object Name -ne 'SHA256SUMS' | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\','/')
    "$(Get-FileHash $_.FullName -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  $relative"
} | Set-Content (Join-Path $output 'SHA256SUMS') -Encoding ascii
Write-Host "Bundle created independently: $output"
