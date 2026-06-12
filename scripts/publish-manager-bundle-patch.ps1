param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\mcp-manager-bundle-patch'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $false,
    [string]$TargetFrameworkOverride = '',
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($TargetFrameworkOverride)) {
    & (Join-Path $repoRoot 'mcp-manager\scripts\publish-win.ps1') `
        -Output $OutputRoot `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -SelfContained $SelfContained `
        -SingleFile $SingleFile
}
else {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('mcp-manager-publish-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
        Copy-Item -Recurse -Path (Join-Path $repoRoot 'mcp-manager\src') -Destination (Join-Path $tempRoot 'src')
        $project = Join-Path $tempRoot 'src\McpManager.csproj'
        $projectXml = Get-Content -Raw -LiteralPath $project
        $projectXml = $projectXml -replace '<TargetFramework>[^<]+</TargetFramework>', "<TargetFramework>$TargetFrameworkOverride</TargetFramework>"
        Set-Content -NoNewline -LiteralPath $project -Value $projectXml

        $publishArgs = @($project, '-c', $Configuration, '-r', $Runtime, '--self-contained', $SelfContained.ToString().ToLowerInvariant(), '-o', $OutputRoot, '/p:UseAppHost=true')
        if ($SingleFile) {
            $publishArgs += '/p:PublishSingleFile=true'
            $publishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
        }
        dotnet publish @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
}

@"
This package contains a replacement McpManager build.

Target framework override: $(if ([string]::IsNullOrWhiteSpace($TargetFrameworkOverride)) { 'none' } else { $TargetFrameworkOverride })

To patch an existing full MCP bundle:
1. Stop running MCP servers.
2. Copy this package's files into the root of mcp-bundle, overwriting existing McpManager files.
3. Run sync-env-files.ps1 once to move editable values from servers.json into per-server .env files.
4. Start servers again.

PowerShell example:
    .\mcp-bundle\McpManager.exe stop all
    Copy-Item -Recurse -Force .\mcp-manager-bundle-patch\* .\mcp-bundle\
    powershell.exe -ExecutionPolicy Bypass -File .\mcp-bundle\sync-env-files.ps1
    .\mcp-bundle\McpManager.exe start all
"@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'README-manager-patch.txt') -Encoding UTF8

Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\sync-bundle-env-files.ps1') -Destination (Join-Path $OutputRoot 'sync-env-files.ps1') -Force

if ($Zip) {
    $zipPath = "$OutputRoot.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $OutputRoot '*') -DestinationPath $zipPath -Force
    Write-Host "Created $zipPath"
}

Write-Host "MCP manager bundle patch is ready in $OutputRoot"
