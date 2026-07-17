param(
    [Parameter(Mandatory = $true)] [string]$Server,
    [Parameter(Mandatory = $true)] [int]$Port,
    [string]$Image = '',
    [string]$ContainerName = '',
    [hashtable]$EnvironmentVariables = @{},
    [string[]]$Volumes = @(),
    [switch]$Remove,
    [switch]$Foreground
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Image)) { $Image = "local/$Server`:latest" }
if ([string]::IsNullOrWhiteSpace($ContainerName)) { $ContainerName = $Server }

$dockerArgs = @('run')
if ($Remove) { $dockerArgs += '--rm' }
if (-not $Foreground) { $dockerArgs += '-d' }
$dockerArgs += @('--name', $ContainerName, '-p', "127.0.0.1:$Port`:8080")

$pathMappings = @()
if ($env:OS -eq 'Windows_NT') {
    foreach ($drive in [System.IO.DriveInfo]::GetDrives()) {
        try {
            if (-not $drive.IsReady -or $drive.RootDirectory.FullName -notmatch '^[A-Za-z]:\\$') { continue }
            $root = $drive.RootDirectory.FullName
            $driveLetter = $root.Substring(0, 1).ToLowerInvariant()
            $containerPath = "/host/drives/$driveLetter"
            $dockerArgs += @('-v', "$($root):$containerPath")
            $pathMappings += "$root=$containerPath"
        }
        catch [System.IO.IOException] { continue }
        catch [System.UnauthorizedAccessException] { continue }
    }
}

foreach ($volume in $Volumes) { $dockerArgs += @('-v', $volume) }
if ($Server -eq 'mcp-docker') {
    $dockerArgs += @('-v', '/var/run/docker.sock:/var/run/docker.sock')
}
if ($Server -eq 'mcp-kubernetes') {
    $kubeDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.kube'
    if (Test-Path -LiteralPath $kubeDirectory) {
        $dockerArgs += @('-v', "$kubeDirectory`:/root/.kube")
    }
}

if ($pathMappings.Count -gt 0 -and -not $EnvironmentVariables.ContainsKey('MCP_PATH_MAPPINGS')) {
    $EnvironmentVariables['MCP_PATH_MAPPINGS'] = $pathMappings -join ';'
}
foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
    $dockerArgs += @('-e', "$($entry.Key)=$($entry.Value)")
}

$dockerArgs += $Image
docker @dockerArgs
if ($LASTEXITCODE -ne 0) { throw "docker run failed for $Server (exit $LASTEXITCODE)." }
