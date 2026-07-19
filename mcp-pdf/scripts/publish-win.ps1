param(
    [string]$Output = (Join-Path $PSScriptRoot '..\publish\mcp-pdf-win-x64'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $false
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\McpPdf.csproj'
$arguments = @($project, '-c', $Configuration, '-r', $Runtime, '--self-contained', $SelfContained.ToString().ToLowerInvariant(), '-o', $Output, '/p:UseAppHost=true')
if ($SingleFile) {
    $arguments += '/p:PublishSingleFile=true'
    $arguments += '/p:IncludeNativeLibrariesForSelfExtract=true'
}
dotnet publish @arguments
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\config\pdf.env.example') -Destination (Join-Path $Output 'pdf.env.example') -Force
Write-Host "Published mcp-pdf to $Output"
