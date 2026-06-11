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
@echo off
setlocal
"%~dp0McpManager.exe" %*
'@ | Set-Content -LiteralPath (Join-Path $Output 'mcp-manager.cmd') -Encoding ASCII
@'
@echo off
setlocal
"%~dp0McpManager.exe" start all
pause
'@ | Set-Content -LiteralPath (Join-Path $Output 'start-all.cmd') -Encoding ASCII
@'
@echo off
setlocal
"%~dp0McpManager.exe" stop all
pause
'@ | Set-Content -LiteralPath (Join-Path $Output 'stop-all.cmd') -Encoding ASCII
@'
@echo off
setlocal
"%~dp0McpManager.exe" status all
pause
'@ | Set-Content -LiteralPath (Join-Path $Output 'status.cmd') -Encoding ASCII

$config = Get-Content -LiteralPath (Join-Path $Output 'servers.json') -Raw | ConvertFrom-Json
foreach ($server in $config.servers) {
@"
@echo off
setlocal
"%~dp0McpManager.exe" start $($server.name)
pause
"@ | Set-Content -LiteralPath (Join-Path $Output "start-$($server.name).cmd") -Encoding ASCII
@"
@echo off
setlocal
"%~dp0McpManager.exe" stop $($server.name)
pause
"@ | Set-Content -LiteralPath (Join-Path $Output "stop-$($server.name).cmd") -Encoding ASCII
}

Write-Host "Published mcp-manager to $Output"
