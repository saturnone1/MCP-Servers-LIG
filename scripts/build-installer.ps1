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
$payloadRoot = Join-Path $objectRoot 'payload'
$launcherProject = Join-Path $installerRoot 'setup-launcher\SetupLauncher.csproj'
$launcherOutputRoot = Join-Path $objectRoot 'setup-launcher'
$uninstallProject = Join-Path $installerRoot 'uninstall-launcher\UninstallLauncher.csproj'
$uninstallOutputRoot = Join-Path $objectRoot 'uninstall-launcher'
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

if (Test-Path -LiteralPath $outputRoot) {
    Get-ChildItem -LiteralPath $outputRoot -File -Filter 'LIG-AI-MCP-Setup-*-*.exe' |
        Remove-Item -Force

    $legacyAdminOutput = Join-Path $outputRoot 'admin'
    if (Test-Path -LiteralPath $legacyAdminOutput) {
        $resolvedLegacyAdminOutput = (Resolve-Path -LiteralPath $legacyAdminOutput).Path
        $expectedLegacyAdminOutput = [IO.Path]::GetFullPath($legacyAdminOutput)
        if ($resolvedLegacyAdminOutput -ne $expectedLegacyAdminOutput) {
            throw "Unexpected legacy installer output path: $resolvedLegacyAdminOutput"
        }
        Remove-Item -LiteralPath $resolvedLegacyAdminOutput -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $outputRoot, $objectRoot, $payloadRoot, $launcherOutputRoot, $uninstallOutputRoot | Out-Null
dotnet publish $uninstallProject `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -o $uninstallOutputRoot `
    -p:SetupVersion=$Version
if ($LASTEXITCODE -ne 0) {
    throw "Elevated uninstaller build failed with exit code $LASTEXITCODE."
}
$publishedUninstaller = Join-Path $uninstallOutputRoot 'LIG-AI-MCP-Uninstall.exe'
if (-not (Test-Path -LiteralPath $publishedUninstaller)) {
    throw "The elevated uninstaller was not published: $publishedUninstaller"
}
Copy-Item -LiteralPath $publishedUninstaller -Destination (Join-Path $bundleRoot 'LIG-AI-MCP-Uninstall.exe') -Force

$msiPath = Join-Path $payloadRoot "LIG-AI-MCP-Payload-$Version-$Runtime.msi"
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
dotnet publish $launcherProject `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -o $launcherOutputRoot `
    -p:SetupVersion=$Version `
    -p:MsiPath=$msiPath
if ($LASTEXITCODE -ne 0) {
    throw "Elevated setup launcher build failed with exit code $LASTEXITCODE."
}
$publishedSetup = Join-Path $launcherOutputRoot 'LIG-AI-MCP-Setup.exe'
if (-not (Test-Path -LiteralPath $publishedSetup)) {
    throw "The elevated setup launcher was not published: $publishedSetup"
}
Copy-Item -LiteralPath $publishedSetup -Destination $setupPath -Force

$bundleBytes = (Get-ChildItem -LiteralPath $bundleRoot -Recurse -File | Measure-Object Length -Sum).Sum
$msi = Get-Item -LiteralPath $msiPath
$setup = Get-Item -LiteralPath $setupPath
Write-Host "Internal MSI payload created: $($msi.FullName)"
Write-Host "Single elevating installer created: $($setup.FullName)"
Write-Host ("Bundle: {0:N1} MiB, MSI: {1:N1} MiB, setup: {2:N1} MiB" -f ($bundleBytes / 1MB), ($msi.Length / 1MB), ($setup.Length / 1MB))
