param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish\mcp-manager-win-x64'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $false
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\McpManager.csproj'

$publishArgs = @($project, '-c', $Configuration, '-r', $Runtime, '--self-contained', $SelfContained.ToString().ToLowerInvariant(), '-o', $Output, '/p:UseAppHost=true')
if ($SingleFile) {
    $publishArgs += '/p:PublishSingleFile=true'
    $publishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
}
dotnet publish @publishArgs

Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\config\servers.json') -Destination (Join-Path $Output 'servers.json') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run.ps1') -Destination (Join-Path $Output 'run.ps1') -Force
@'
param(
    [string]$FontName = 'Noto Sans KR'
)

$ErrorActionPreference = 'Stop'
$fontSource = Join-Path $PSScriptRoot 'fonts\NotoSansKR[wght].ttf'
if (-not (Test-Path -LiteralPath $fontSource)) {
    throw "Font file not found: $fontSource"
}

$fontInstallDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts'
New-Item -ItemType Directory -Force -Path $fontInstallDir | Out-Null
$fontFileName = Split-Path -Leaf $fontSource
$fontTarget = Join-Path $fontInstallDir $fontFileName
Copy-Item -LiteralPath $fontSource -Destination $fontTarget -Force

$registryPath = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'
New-Item -Path $registryPath -Force | Out-Null
New-ItemProperty -Path $registryPath -Name "$FontName (TrueType)" -Value $fontFileName -PropertyType String -Force | Out-Null

Write-Host "$FontName installed for the current user."
Write-Host "Restart Windows Terminal/CMD, then select '$FontName' in the terminal font settings if needed."
'@ | Set-Content -LiteralPath (Join-Path $Output 'install-fonts.ps1') -Encoding UTF8
@'
@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-fonts.ps1"
pause
'@ | Set-Content -LiteralPath (Join-Path $Output 'install-fonts.cmd') -Encoding ASCII
@'
@echo off
setlocal
start "" /wait "%~dp0McpManager.exe" %*
'@ | Set-Content -LiteralPath (Join-Path $Output 'mcp-manager.cmd') -Encoding ASCII
@'
@echo off
setlocal
start "" /wait "%~dp0McpManager.exe" %*
'@ | Set-Content -LiteralPath (Join-Path $Output 'LIG-AI-MCP.cmd') -Encoding ASCII
@'
@echo off
setlocal
start "" /wait "%~dp0McpManager.exe" start all
pause
'@ | Set-Content -LiteralPath (Join-Path $Output 'start-all.cmd') -Encoding ASCII
@'
@echo off
setlocal
start "" /wait "%~dp0McpManager.exe" stop all
pause
'@ | Set-Content -LiteralPath (Join-Path $Output 'stop-all.cmd') -Encoding ASCII
@'
@echo off
setlocal
start "" /wait "%~dp0McpManager.exe" status all
pause
'@ | Set-Content -LiteralPath (Join-Path $Output 'status.cmd') -Encoding ASCII

$config = Get-Content -LiteralPath (Join-Path $Output 'servers.json') -Raw | ConvertFrom-Json
foreach ($server in $config.servers) {
@"
@echo off
setlocal
start "" /wait "%~dp0McpManager.exe" start $($server.name)
pause
"@ | Set-Content -LiteralPath (Join-Path $Output "start-$($server.name).cmd") -Encoding ASCII
@"
@echo off
setlocal
start "" /wait "%~dp0McpManager.exe" stop $($server.name)
pause
"@ | Set-Content -LiteralPath (Join-Path $Output "stop-$($server.name).cmd") -Encoding ASCII
}

Write-Host "Published mcp-manager to $Output"
