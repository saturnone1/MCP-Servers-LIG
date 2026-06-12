param()

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')

function Get-MethodBody {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $MethodName
    )

    $match = [regex]::Match($Text, "(?s)\b(?:public|private|internal)\s+static\s+[^{;=]+?\s+$([regex]::Escape($MethodName))\s*\([^)]*\)\s*(?:=>\s*[^;]+;|\{)")
    if (-not $match.Success) {
        return $null
    }

    if ($match.Value.Contains('=>')) {
        return $match.Value
    }

    $braceStart = $Text.IndexOf('{', $match.Index)
    if ($braceStart -lt 0) {
        return $null
    }

    $depth = 0
    for ($i = $braceStart; $i -lt $Text.Length; $i++) {
        if ($Text[$i] -eq '{') { $depth++ }
        elseif ($Text[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return $Text.Substring($braceStart, $i - $braceStart + 1)
            }
        }
    }

    throw "Could not parse method body for $MethodName"
}

$failures = [System.Collections.Generic.List[string]]::new()

$safeQueryMethods = @(
    @{ Path = 'mcp-autocad/src/Program.cs'; Methods = @('Config', 'DetectInstallations') },
    @{ Path = 'mcp-matlab/src/Program.cs'; Methods = @('Config', 'DetectInstallations') },
    @{ Path = 'mcp-solidworks/src/Program.cs'; Methods = @('Config', 'DetectInstallations') },
    @{ Path = 'mcp-rhapsody/src/Program.cs'; Methods = @('Config', 'DetectInstallations') }
)

$forbiddenInSafeQueries = @(
    'Activator.CreateInstance',
    'Com.GetOrCreate',
    'RhapsodyCom.GetApplication',
    'AutoCad.Application',
    'SolidWorks.Application',
    'Process.Start',
    'CommandRunner.Run',
    'OfficeCli('
)

foreach ($entry in $safeQueryMethods) {
    $path = Join-Path $repo $entry.Path
    $text = Get-Content -LiteralPath $path -Raw
    foreach ($method in $entry.Methods) {
        $body = Get-MethodBody -Text $text -MethodName $method
        if ($null -eq $body) {
            $failures.Add("$($entry.Path): method not found: $method")
            continue
        }

        foreach ($pattern in $forbiddenInSafeQueries) {
            if ($body.Contains($pattern, [StringComparison]::Ordinal)) {
                $failures.Add("$($entry.Path): $method contains side-effectful call '$pattern'")
            }
        }
    }
}

$desktopComFiles = @(
    'mcp-autocad/src/Program.cs',
    'mcp-matlab/src/Program.cs',
    'mcp-solidworks/src/Program.cs',
    'mcp-rhapsody/src/Program.cs'
)

foreach ($relativePath in $desktopComFiles) {
    $text = Get-Content -LiteralPath (Join-Path $repo $relativePath) -Raw
    foreach ($required in @('IsRegistered', 'TryGetActive', 'GetOrCreate', 'Create')) {
        if (-not $text.Contains($required, [StringComparison]::Ordinal)) {
            $failures.Add("${relativePath}: missing COM helper '$required'")
        }
    }
}

$rhapsodyText = Get-Content -LiteralPath (Join-Path $repo 'mcp-rhapsody/src/Program.cs') -Raw
$getApplication = Get-MethodBody -Text $rhapsodyText -MethodName 'GetApplication'
if ($null -eq $getApplication -or -not $getApplication.Contains('GetOrCreate(ResolveProgId())', [StringComparison]::Ordinal)) {
    $failures.Add('mcp-rhapsody/src/Program.cs: GetApplication should reuse an active COM object before creating one')
}

foreach ($program in Get-ChildItem -Path $repo -Directory -Filter 'mcp-*') {
    $programPath = Join-Path $program.FullName 'src/Program.cs'
    if (-not (Test-Path -LiteralPath $programPath)) { continue }
    $text = Get-Content -LiteralPath $programPath -Raw
    $healthLine = ($text -split "`n") | Where-Object { $_ -match 'MapGet\("/healthz"' }
    if ($healthLine -match 'Process\.Start|Activator\.CreateInstance|GetOrCreate|CommandRunner\.Run|OfficeCli\(') {
        $failures.Add("$($program.Name): /healthz maps a side-effectful operation")
    }
}

if ($failures.Count -gt 0) {
    Write-Host 'Static side-effect scan failed:' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host 'Static side-effect scan passed.' -ForegroundColor Green
