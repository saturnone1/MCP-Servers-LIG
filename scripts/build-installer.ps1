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
$adminOutputRoot = Join-Path $outputRoot 'admin'
$objectRoot = Join-Path $installerRoot 'obj'
$bundleObjectRoot = Join-Path $objectRoot 'bundle'
$iconPng = Join-Path $repoRoot 'mcp-manager\src\assets\mcp-manager-icon-preview.png'
$iconIco = Join-Path $repoRoot 'mcp-manager\src\assets\mcp-manager.ico'
$wixRoot = Join-Path $repoRoot '.tools\wix'
$wix = Join-Path $wixRoot 'wix.exe'
$bootstrapperExtension = 'WixToolset.BootstrapperApplications.wixext'
$bootstrapperExtensionVersion = '5.0.2'

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

$installedExtensions = @(& $wix extension list 2>$null)
if (-not ($installedExtensions | Where-Object { $_ -match "^$([regex]::Escape($bootstrapperExtension))\s" })) {
    & $wix extension add "$bootstrapperExtension/$bootstrapperExtensionVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install the local WiX bootstrapper extension (exit $LASTEXITCODE)."
    }
}

New-Item -ItemType Directory -Force -Path $outputRoot, $adminOutputRoot, $objectRoot, $bundleObjectRoot | Out-Null
$msiPath = Join-Path $adminOutputRoot "LIG-AI-MCP-Admin-Deploy-$Version-$Runtime.msi"
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

$setupPath = Join-Path $outputRoot "LIG-AI-MCP-Setup-$Version-$Runtime.exe"
& $wix build `
    (Join-Path $installerRoot 'Bundle.wxs') `
    -arch x64 `
    -ext $bootstrapperExtension `
    -d "ProductVersion=$Version" `
    -d "MsiPath=$msiPath" `
    -d "IconPath=$iconIco" `
    -d "LogoPath=$iconPng" `
    -intermediateFolder $bundleObjectRoot `
    -defaultCompressionLevel high `
    -pdbType none `
    -out $setupPath
if ($LASTEXITCODE -ne 0) {
    throw "WiX bootstrapper build failed with exit code $LASTEXITCODE."
}

$bundleBytes = (Get-ChildItem -LiteralPath $bundleRoot -Recurse -File | Measure-Object Length -Sum).Sum
$msi = Get-Item -LiteralPath $msiPath
$setup = Get-Item -LiteralPath $setupPath
Write-Host "Installer created: $($msi.FullName)"
Write-Host "Elevating setup created: $($setup.FullName)"
Write-Host ("Bundle: {0:N1} MiB, MSI: {1:N1} MiB, setup: {2:N1} MiB" -f ($bundleBytes / 1MB), ($msi.Length / 1MB), ($setup.Length / 1MB))
