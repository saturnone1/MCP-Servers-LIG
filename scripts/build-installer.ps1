param(
    [string] $Version = '1.0.0',
    [string] $Runtime = 'win-x64',
    [switch] $SkipBundle
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$bundleRoot = Join-Path $repoRoot 'mcp-bundle'
$installerRoot = Join-Path $repoRoot 'installer'
$outputRoot = Join-Path $installerRoot 'output'
$objectRoot = Join-Path $installerRoot 'obj'
$iconPng = Join-Path $repoRoot 'mcp-manager\src\assets\mcp-manager-icon-preview.png'
$iconIco = Join-Path $repoRoot 'mcp-manager\src\assets\mcp-manager.ico'
$wixRoot = Join-Path $repoRoot '.tools\wix'
$wix = Join-Path $wixRoot 'wix.exe'

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "Version must contain three or four numeric parts: $Version"
}

& (Join-Path $PSScriptRoot 'convert-png-to-ico.ps1') -InputPath $iconPng -OutputPath $iconIco

if (-not $SkipBundle) {
    & (Join-Path $PSScriptRoot 'publish-mcp-bundle.ps1') -Runtime $Runtime
}
if (-not (Test-Path -LiteralPath (Join-Path $bundleRoot 'McpManager.exe'))) {
    throw 'The MCP bundle is missing. Run without -SkipBundle to publish it first.'
}

if (-not (Test-Path -LiteralPath $wix)) {
    New-Item -ItemType Directory -Force -Path $wixRoot | Out-Null
    dotnet tool install wix --version 5.0.2 --tool-path $wixRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install the local WiX build tool (exit $LASTEXITCODE)."
    }
}

New-Item -ItemType Directory -Force -Path $outputRoot, $objectRoot | Out-Null
$msiPath = Join-Path $outputRoot "LIG-AI-MCP-Setup-$Version-$Runtime.msi"
& $wix build `
    (Join-Path $installerRoot 'Product.wxs') `
    -arch x64 `
    -d "ProductVersion=$Version" `
    -d "BundleDirectory=$bundleRoot" `
    -d "IconPath=$iconIco" `
    -intermediateFolder $objectRoot `
    -defaultCompressionLevel high `
    -pdbType none `
    -out $msiPath
if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE."
}

$bundleBytes = (Get-ChildItem -LiteralPath $bundleRoot -Recurse -File | Measure-Object Length -Sum).Sum
$msi = Get-Item -LiteralPath $msiPath
Write-Host "Installer created: $($msi.FullName)"
Write-Host ("Bundle: {0:N1} MiB, installer: {1:N1} MiB" -f ($bundleBytes / 1MB), ($msi.Length / 1MB))
