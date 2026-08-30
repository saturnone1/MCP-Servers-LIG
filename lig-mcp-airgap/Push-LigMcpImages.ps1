param([Parameter(Mandatory)][string]$Registry)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$definition = Get-Content (Join-Path $root 'images.json') -Raw | ConvertFrom-Json
foreach ($item in $definition.images) {
    docker load -i (Join-Path $root "images/$($item.name).tar")
    if ($LASTEXITCODE) { throw "Load failed: $($item.name)" }
    $target = "$($Registry.TrimEnd('/'))/$($item.target)"
    docker tag $item.source $target
    docker push $target
    if ($LASTEXITCODE) { throw "Push failed: $target" }
}
