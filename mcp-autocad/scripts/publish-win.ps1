param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish\mcp-autocad-win-x64'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $false
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\McpAutoCad.csproj'
$publishArgs = @($project, '-c', $Configuration, '-r', $Runtime, '--self-contained', $SelfContained.ToString().ToLowerInvariant(), '-o', $Output, '/p:UseAppHost=true')
if ($SingleFile) {
    $publishArgs += '/p:PublishSingleFile=true'
    $publishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
}
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run.ps1') -Destination (Join-Path $Output 'run.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\config\autocad.env.example') -Destination (Join-Path $Output 'autocad.env.example') -Force
if (-not (Test-Path -LiteralPath (Join-Path $Output 'autocad.env'))) {
    Copy-Item -LiteralPath (Join-Path $Output 'autocad.env.example') -Destination (Join-Path $Output 'autocad.env') -Force
}
@'
@echo off
setlocal
powershell.exe -NoExit -ExecutionPolicy Bypass -File "%~dp0run.ps1"
'@ | Set-Content -LiteralPath (Join-Path $Output 'start.cmd') -Encoding ASCII
Write-Host "Published mcp-autocad to $Output"
