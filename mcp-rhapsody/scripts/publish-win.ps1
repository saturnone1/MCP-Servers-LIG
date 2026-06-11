param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish\mcp-rhapsody-win-x64'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $false
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\McpRhapsody.csproj'

$publishArgs = @($project, '-c', $Configuration, '-r', $Runtime, '--self-contained', $SelfContained.ToString().ToLowerInvariant(), '-o', $Output, '/p:UseAppHost=true')
if ($SingleFile) {
    $publishArgs += '/p:PublishSingleFile=true'
    $publishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
}
dotnet publish @publishArgs

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run.ps1') -Destination (Join-Path $Output 'run.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\config\rhapsody.env.example') -Destination (Join-Path $Output 'rhapsody.env.example') -Force
if (-not (Test-Path -LiteralPath (Join-Path $Output 'rhapsody.env'))) {
    Copy-Item -LiteralPath (Join-Path $Output 'rhapsody.env.example') -Destination (Join-Path $Output 'rhapsody.env') -Force
}
@'
@echo off
setlocal
powershell.exe -NoExit -ExecutionPolicy Bypass -File "%~dp0run.ps1"
'@ | Set-Content -LiteralPath (Join-Path $Output 'start.cmd') -Encoding ASCII

Write-Host "Published mcp-rhapsody to $Output"
Write-Host "Airgap usage: copy this folder to the Windows Rhapsody machine and run .\start.cmd, .\run.ps1, or .\McpRhapsody.exe"
