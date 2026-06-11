param(
    [string]$TargetRunScript = (Join-Path $PSScriptRoot '..\publish\mcp-rhapsody-win-x64\run.ps1'),
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'mcp-rhapsody.lnk')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $TargetRunScript)) {
    throw "Run script not found: $TargetRunScript. Run scripts\publish-win.ps1 first or pass -TargetRunScript."
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($ShortcutPath)
$shortcut.TargetPath = 'powershell.exe'
$shortcut.Arguments = "-NoExit -ExecutionPolicy Bypass -File `"$TargetRunScript`""
$shortcut.WorkingDirectory = Split-Path -Parent $TargetRunScript
$shortcut.IconLocation = 'powershell.exe,0'
$shortcut.Save()

Write-Host "Created shortcut: $ShortcutPath"

