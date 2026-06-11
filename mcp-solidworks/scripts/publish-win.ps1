param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish\mcp-solidworks-win-x64'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $false
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\McpSolidWorks.csproj'
$publishArgs = @($project, '-c', $Configuration, '-r', $Runtime, '--self-contained', $SelfContained.ToString().ToLowerInvariant(), '-o', $Output, '/p:UseAppHost=true')
if ($SingleFile) {
    $publishArgs += '/p:PublishSingleFile=true'
    $publishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
}
dotnet publish @publishArgs
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run.ps1') -Destination (Join-Path $Output 'run.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\config\solidworks.env.example') -Destination (Join-Path $Output 'solidworks.env.example') -Force
if (-not (Test-Path -LiteralPath (Join-Path $Output 'solidworks.env'))) {
    Copy-Item -LiteralPath (Join-Path $Output 'solidworks.env.example') -Destination (Join-Path $Output 'solidworks.env') -Force
}
@'
@echo off
setlocal
powershell.exe -NoExit -ExecutionPolicy Bypass -File "%~dp0run.ps1"
'@ | Set-Content -LiteralPath (Join-Path $Output 'start.cmd') -Encoding ASCII
Write-Host "Published mcp-solidworks to $Output"
