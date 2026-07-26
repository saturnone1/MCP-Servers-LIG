param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\windows-host-publish'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $false,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$servers = @(
    @{ Name = 'mcp-rhapsody'; Script = 'mcp-rhapsody\scripts\publish-win.ps1'; Folder = 'mcp-rhapsody-win-x64' },
    @{ Name = 'mcp-matlab'; Script = 'mcp-matlab\scripts\publish-win.ps1'; Folder = 'mcp-matlab-win-x64' },
    @{ Name = 'mcp-autocad'; Script = 'mcp-autocad\scripts\publish-win.ps1'; Folder = 'mcp-autocad-win-x64' },
    @{ Name = 'mcp-solidworks'; Script = 'mcp-solidworks\scripts\publish-win.ps1'; Folder = 'mcp-solidworks-win-x64' }
)

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$stalePdfOutput = [IO.Path]::GetFullPath((Join-Path $OutputRoot 'mcp-pdf-win-x64'))
$outputPrefix = [IO.Path]::GetFullPath($OutputRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $stalePdfOutput.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected retired artifact path: $stalePdfOutput"
}
if (Test-Path -LiteralPath $stalePdfOutput) {
    $stalePdfExe = Join-Path $stalePdfOutput 'McpPdf.exe'
    if (Test-Path -LiteralPath $stalePdfExe) {
        $resolvedExe = (Resolve-Path -LiteralPath $stalePdfExe).Path
        Get-Process -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and ([string]::Equals($_.Path, $resolvedExe, [StringComparison]::OrdinalIgnoreCase)) }
            catch { $false }
        } | ForEach-Object {
            Write-Host ("Stopping retired mcp-pdf PID {0}" -f $_.Id)
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            try { Wait-Process -Id $_.Id -Timeout 10 -ErrorAction SilentlyContinue } catch { }
        }
    }
    Remove-Item -LiteralPath $stalePdfOutput -Recurse -Force
}

foreach ($server in $servers) {
    $publishScript = Join-Path $repoRoot $server.Script
    $output = Join-Path $OutputRoot $server.Folder
    Write-Host ""
    Write-Host "== Publishing $($server.Name) to $output"
    & $publishScript -Output $output -Configuration $Configuration -Runtime $Runtime -SelfContained $SelfContained -SingleFile $SingleFile
}

if ($Zip) {
    foreach ($server in $servers) {
        $folder = Join-Path $OutputRoot $server.Folder
        $zipPath = Join-Path $OutputRoot "$($server.Folder).zip"
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }
        Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zipPath -Force
        Write-Host "Created $zipPath"
    }
}

Write-Host ""
Write-Host "Windows-host MCP packages are ready in $OutputRoot"
