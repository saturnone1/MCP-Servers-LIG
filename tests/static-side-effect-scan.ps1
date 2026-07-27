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

function Test-ContainsOrdinal {
    param(
        [AllowNull()][string]$Text,
        [Parameter(Mandatory = $true)][string]$Value
    )

    return $null -ne $Text -and $Text.IndexOf($Value, [StringComparison]::Ordinal) -ge 0
}

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
            if (Test-ContainsOrdinal $body $pattern) {
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
        if (-not (Test-ContainsOrdinal $text $required)) {
            $failures.Add("${relativePath}: missing COM helper '$required'")
        }
    }
}

$rhapsodyText = Get-Content -LiteralPath (Join-Path $repo 'mcp-rhapsody/src/Program.cs') -Raw
$getApplication = Get-MethodBody -Text $rhapsodyText -MethodName 'GetApplication'
if ($null -eq $getApplication -or -not (Test-ContainsOrdinal $getApplication 'GetOrCreate(ResolveProgId())')) {
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

$unsafePathPrefixPatterns = @(
    'StartsWith(Path.GetFullPath(root)',
    'StartsWith(root, StringComparison'
)

foreach ($program in Get-ChildItem -Path $repo -Directory -Filter 'mcp-*') {
    $programPath = Join-Path $program.FullName 'src/Program.cs'
    if (-not (Test-Path -LiteralPath $programPath)) { continue }
    $text = Get-Content -LiteralPath $programPath -Raw
    foreach ($pattern in $unsafePathPrefixPatterns) {
        if (Test-ContainsOrdinal $text $pattern) {
            $failures.Add("$($program.Name): path allow-list uses unsafe prefix check '$pattern'")
        }
    }
}

foreach ($entry in @(
    @{ Path = 'mcp-mssql/src/Program.cs'; Method = 'RequireReadQuery'; Required = @('HasMultipleStatements', 'ContainsWriteKeyword') },
    @{ Path = 'mcp-postgresql/src/Program.cs'; Method = 'RequireReadQuery'; Required = @('HasMultipleStatements', 'ContainsWriteKeyword') }
)) {
    $text = Get-Content -LiteralPath (Join-Path $repo $entry.Path) -Raw
    $body = Get-MethodBody -Text $text -MethodName $entry.Method
    if ($null -eq $body) {
        $failures.Add("$($entry.Path): method not found: $($entry.Method)")
        continue
    }

    foreach ($required in $entry.Required) {
        if (-not (Test-ContainsOrdinal $body $required)) {
            $failures.Add("$($entry.Path): $($entry.Method) does not call '$required'")
        }
    }
}

foreach ($entry in @(
    @{ Path = 'mcp-hwp/src/Program.cs'; Method = 'Convert'; Required = @('Guard.RequireWrites') },
    @{ Path = 'mcp-matlab/src/Program.cs'; Method = 'RunBatch'; Required = @('Guard.RequireWrites') },
    @{ Path = 'mcp-matlab/src/Program.cs'; Method = 'EvalCommand'; Required = @('Guard.RequireWrites') },
    @{ Path = 'mcp-dotnet/src/Program.cs'; Method = 'Restore'; Required = @('Guard.RequireDotnetWrites') },
    @{ Path = 'mcp-dotnet/src/Program.cs'; Method = 'Build'; Required = @('Guard.RequireDotnetWrites') },
    @{ Path = 'mcp-dotnet/src/Program.cs'; Method = 'Test'; Required = @('Guard.RequireDotnetWrites') },
    @{ Path = 'mcp-kubernetes/src/Program.cs'; Method = 'RunKubectl'; Required = @('Guard.RequireKubernetesWrites', 'Guard.RequireRawKubectl') }
)) {
    $text = Get-Content -LiteralPath (Join-Path $repo $entry.Path) -Raw
    $body = Get-MethodBody -Text $text -MethodName $entry.Method
    if ($null -eq $body) {
        $failures.Add("$($entry.Path): method not found: $($entry.Method)")
        continue
    }

    foreach ($required in $entry.Required) {
        if (-not (Test-ContainsOrdinal $body $required)) {
            $failures.Add("$($entry.Path): $($entry.Method) does not call '$required'")
        }
    }
}

$matlabText = Get-Content -LiteralPath (Join-Path $repo 'mcp-matlab/src/Program.cs') -Raw
$listWorkspace = Get-MethodBody -Text $matlabText -MethodName 'ListWorkspace'
if ($null -eq $listWorkspace -or -not (Test-ContainsOrdinal $listWorkspace 'Com.TryGetActive') -or (Test-ContainsOrdinal $listWorkspace 'Com.GetOrCreate')) {
    $failures.Add('mcp-matlab/src/Program.cs: ListWorkspace should reuse only an active COM object and must not create MATLAB')
}

foreach ($entry in @(
    @{ Path = 'mcp-gitlab/src/Program.cs'; Method = 'Send'; Required = 'UriFormatException' },
    @{ Path = 'mcp-jira/src/Program.cs'; Method = 'Send'; Required = 'UriFormatException' },
    @{ Path = 'mcp-confluence/src/Program.cs'; Method = 'Send'; Required = 'UriFormatException' },
    @{ Path = 'mcp-prometheus/src/Program.cs'; Method = 'Get'; Required = 'UriFormatException' },
    @{ Path = 'mcp-loki/src/Program.cs'; Method = 'Get'; Required = 'UriFormatException' }
)) {
    $text = Get-Content -LiteralPath (Join-Path $repo $entry.Path) -Raw
    $body = Get-MethodBody -Text $text -MethodName $entry.Method
    if ($null -eq $body -or -not (Test-ContainsOrdinal $body $entry.Required)) {
        $failures.Add("$($entry.Path): $($entry.Method) should return a structured failure for malformed base URLs")
    }
}

$confluenceText = Get-Content -LiteralPath (Join-Path $repo 'mcp-confluence/src/Program.cs') -Raw
$confluenceBuildUri = Get-MethodBody -Text $confluenceText -MethodName 'BuildUri'
if ($null -eq $confluenceBuildUri -or -not (Test-ContainsOrdinal $confluenceBuildUri 'basePath')) {
    $failures.Add('mcp-confluence/src/Program.cs: BuildUri should preserve CONFLUENCE_BASE_URL context paths such as /confluence')
}

$confluenceServerInfo = Get-MethodBody -Text $confluenceText -MethodName 'ServerInfo'
if ($null -eq $confluenceServerInfo -or
    -not (Test-ContainsOrdinal $confluenceServerInfo '/rest/api/settings/systemInfo') -or
    -not (Test-ContainsOrdinal $confluenceServerInfo '/rest/troubleshooting/1.0/pre-upgrade/info')) {
    $failures.Add('mcp-confluence/src/Program.cs: ServerInfo should support both modern server information and 6.15.8+ troubleshooting version endpoints')
}

foreach ($required in @('CONFLUENCE_PAT', 'CONFLUENCE_COOKIE')) {
    if (-not (Test-ContainsOrdinal $confluenceText $required)) {
        $failures.Add("mcp-confluence/src/Program.cs: missing compatibility auth variable '$required'")
    }
}

$portConfigFiles = @(
    'mcp-manager/config/servers.json',
    'mcp-manager/config/servers.bundle.json'
)
$expectedServers = @(
    'mcp-office', 'mcp-filesystem', 'mcp-git', 'mcp-shell', 'mcp-dotnet', 'mcp-mssql',
    'mcp-hwp', 'mcp-kubernetes', 'mcp-docker', 'mcp-prometheus', 'mcp-postgresql',
    'mcp-gitlab', 'mcp-jira', 'mcp-loki', 'mcp-confluence', 'mcp-rhapsody',
    'mcp-matlab', 'mcp-autocad', 'mcp-solidworks'
)
foreach ($relativePath in $portConfigFiles) {
    $config = Get-Content -LiteralPath (Join-Path $repo $relativePath) -Raw | ConvertFrom-Json
    $actualServers = @($config.servers | ForEach-Object { $_.name })
    $missingServers = @($expectedServers | Where-Object { $_ -notin $actualServers })
    $unexpectedServers = @($actualServers | Where-Object { $_ -notin $expectedServers })
    if ($actualServers.Count -ne $expectedServers.Count -or $missingServers.Count -gt 0 -or $unexpectedServers.Count -gt 0) {
        $failures.Add("${relativePath}: expected the 19-server release set; missing=[$($missingServers -join ',')] unexpected=[$($unexpectedServers -join ',')]")
    }
    if (@($actualServers | Select-Object -Unique).Count -ne $actualServers.Count) {
        $failures.Add("${relativePath}: contains duplicate server names")
    }
    $actualPorts = @($config.servers | ForEach-Object { [int]$_.port })
    $expectedPorts = @(42180..42198)
    $missingPorts = @($expectedPorts | Where-Object { $_ -notin $actualPorts })
    $unexpectedPorts = @($actualPorts | Where-Object { $_ -notin $expectedPorts })
    if ($missingPorts.Count -gt 0 -or $unexpectedPorts.Count -gt 0 -or
        @($actualPorts | Select-Object -Unique).Count -ne $actualPorts.Count) {
        $failures.Add("${relativePath}: expected unique ports 42180-42198; missing=[$($missingPorts -join ',')] unexpected=[$($unexpectedPorts -join ',')]")
    }
    foreach ($server in $config.servers) {
        if ($null -eq $server.env -or @($server.env.PSObject.Properties).Count -eq 0) {
            $failures.Add("${relativePath}: $($server.name) must publish editable environment defaults")
        }
        if ($server.port -ge 8080 -and $server.port -le 8098) {
            $failures.Add("${relativePath}: $($server.name) uses collision-prone external port $($server.port); use the 42180-42198 range")
        }
    }
}

$ignoredRuntimeEnv = @('ASPNETCORE_URLS', 'PATH', 'PATHEXT', 'DOTNET_HOST_PATH', 'DOTNET_ROOT', 'COMSPEC')
$dynamicServerEnv = @{
    'mcp-shell' = @('MCP_SHELL_ALLOWED_COMMANDS', 'MCP_SHELL_ALLOWED_ENV')
    'mcp-dotnet' = @('MCP_DOTNET_CLI_PATH')
}
foreach ($relativePath in $portConfigFiles) {
    $environmentCatalog = Get-Content -LiteralPath (Join-Path $repo $relativePath) -Raw | ConvertFrom-Json
    foreach ($server in $environmentCatalog.servers) {
        $sourcePath = Join-Path $repo "$($server.name)\src\Program.cs"
        $sourceText = Get-Content -LiteralPath $sourcePath -Raw
        $requiredEnv = @([regex]::Matches($sourceText, 'GetEnvironmentVariable\("([A-Z][A-Z0-9_]*)"\)') |
            ForEach-Object { $_.Groups[1].Value } |
            Where-Object { $_ -notin $ignoredRuntimeEnv })
        if ($dynamicServerEnv.ContainsKey($server.name)) {
            $requiredEnv += $dynamicServerEnv[$server.name]
        }
        $publishedEnv = @($server.env.PSObject.Properties.Name)
        $missingEnv = @($requiredEnv | Select-Object -Unique | Where-Object { $_ -notin $publishedEnv })
        if ($missingEnv.Count -gt 0) {
            $failures.Add("${relativePath}: $($server.name) omits supported environment variables [$($missingEnv -join ',')]")
        }
    }
}

$bundlePublishText = Get-Content -LiteralPath (Join-Path $repo 'scripts/publish-mcp-bundle.ps1') -Raw
$stopIndex = $bundlePublishText.IndexOf('Stop-ExistingBundleProcesses -BundleRoot $OutputRoot', [StringComparison]::Ordinal)
$retiredCleanupIndex = $bundlePublishText.IndexOf("foreach (`$relativePath in @('mcp-pdf-win-x64', 'dependencies\poppler'))", [StringComparison]::Ordinal)
if ($stopIndex -lt 0 -or $retiredCleanupIndex -le $stopIndex) {
    $failures.Add('scripts/publish-mcp-bundle.ps1: running legacy servers must stop before retired MCP-PDF artifacts are removed')
}
if (Test-ContainsOrdinal $bundlePublishText '$server.env = [pscustomobject]@{}') {
    $failures.Add('scripts/publish-mcp-bundle.ps1: published environment defaults must remain in servers.json')
}

$readmeText = Get-Content -LiteralPath (Join-Path $repo 'README.md') -Raw
if (Test-ContainsOrdinal $readmeText 'registers all 20 servers') {
    $failures.Add('README.md: bundle server count is stale; the release contains 19 servers')
}

$managerText = Get-Content -LiteralPath (Join-Path $repo 'mcp-manager/src/Program.cs') -Raw
foreach ($required in @(
    'JobObjectLimitKillOnJobClose',
    'AssignProcessToJobObject',
    'StartAutostartServers',
    'autostart.json',
    'ToggleAutostart'
)) {
    if (-not (Test-ContainsOrdinal $managerText $required)) {
        $failures.Add("mcp-manager/src/Program.cs: missing manager lifecycle/autostart behavior '$required'")
    }
}

$defaultEnvIndex = $managerText.IndexOf('foreach (var env in server.Env)', [StringComparison]::Ordinal)
$envFileIndex = $managerText.IndexOf('foreach (var envFile in ResolveEnvFiles(server))', [StringComparison]::Ordinal)
if ($defaultEnvIndex -lt 0 -or $envFileIndex -lt 0 -or $defaultEnvIndex -gt $envFileIndex) {
    $failures.Add('mcp-manager/src/Program.cs: user env files must override server environment defaults')
}
if (-not (Test-ContainsOrdinal $managerText 'existingKeys.Add(pair.Key)')) {
    $failures.Add('mcp-manager/src/Program.cs: editable env files must merge newly published defaults during upgrades')
}

if ($failures.Count -gt 0) {
    Write-Host 'Static side-effect scan failed:' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host 'Static side-effect scan passed.' -ForegroundColor Green
