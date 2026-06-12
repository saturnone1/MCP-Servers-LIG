param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\mcp-autocad-bundle-patch'),
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [bool]$SelfContained = $true,
    [bool]$SingleFile = $false,
    [string]$TargetFrameworkOverride = '',
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$serverFolder = 'mcp-autocad-win-x64'
$serverOutput = Join-Path $OutputRoot $serverFolder

if (Test-Path -LiteralPath $serverOutput) {
    Remove-Item -LiteralPath $serverOutput -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($TargetFrameworkOverride)) {
    & (Join-Path $repoRoot 'mcp-autocad\scripts\publish-win.ps1') `
        -Output $serverOutput `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -SelfContained $SelfContained `
        -SingleFile $SingleFile
}
else {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('mcp-autocad-publish-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
        Copy-Item -Recurse -Path (Join-Path $repoRoot 'mcp-autocad\src') -Destination (Join-Path $tempRoot 'src')
        $project = Join-Path $tempRoot 'src\McpAutoCad.csproj'
        $projectXml = Get-Content -Raw -LiteralPath $project
        $projectXml = $projectXml -replace '<TargetFramework>[^<]+</TargetFramework>', "<TargetFramework>$TargetFrameworkOverride</TargetFramework>"
        Set-Content -NoNewline -LiteralPath $project -Value $projectXml

        $publishArgs = @($project, '-c', $Configuration, '-r', $Runtime, '--self-contained', $SelfContained.ToString().ToLowerInvariant(), '-o', $serverOutput, '/p:UseAppHost=true')
        if ($SingleFile) {
            $publishArgs += '/p:PublishSingleFile=true'
            $publishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
        }
        dotnet publish @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }

        Copy-Item -LiteralPath (Join-Path $repoRoot 'mcp-autocad\scripts\run.ps1') -Destination (Join-Path $serverOutput 'run.ps1') -Force
        Copy-Item -LiteralPath (Join-Path $repoRoot 'mcp-autocad\config\autocad.env.example') -Destination (Join-Path $serverOutput 'autocad.env.example') -Force
        if (-not (Test-Path -LiteralPath (Join-Path $serverOutput 'autocad.env'))) {
            Copy-Item -LiteralPath (Join-Path $serverOutput 'autocad.env.example') -Destination (Join-Path $serverOutput 'autocad.env') -Force
        }
        @'
@echo off
setlocal
powershell.exe -NoExit -ExecutionPolicy Bypass -File "%~dp0run.ps1"
'@ | Set-Content -LiteralPath (Join-Path $serverOutput 'start.cmd') -Encoding ASCII
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
}

$serverExe = Join-Path $serverOutput 'McpAutoCad.exe'
if (-not (Test-Path -LiteralPath $serverExe)) {
    throw "AutoCAD MCP publish did not produce $serverExe."
}

@"
This package contains only $serverFolder.

Target framework override: $(if ([string]::IsNullOrWhiteSpace($TargetFrameworkOverride)) { 'none' } else { $TargetFrameworkOverride })

To patch an existing full MCP bundle:
1. Stop mcp-autocad from the bundle manager.
2. Replace mcp-bundle\$serverFolder with this $serverFolder folder.
3. Start mcp-autocad again.

PowerShell example:
    .\mcp-bundle\McpManager.exe stop mcp-autocad
    Remove-Item -Recurse -Force .\mcp-bundle\$serverFolder
    Copy-Item -Recurse .\$serverFolder .\mcp-bundle\$serverFolder
    .\mcp-bundle\McpManager.exe start mcp-autocad
"@ | Set-Content -LiteralPath (Join-Path $OutputRoot 'README.txt') -Encoding UTF8

if ($Zip) {
    $zipPath = "$OutputRoot.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $OutputRoot '*') -DestinationPath $zipPath -Force
    Write-Host "Created $zipPath"
}

Write-Host "AutoCAD MCP bundle patch is ready in $OutputRoot"
