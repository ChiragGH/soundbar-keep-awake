$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\SoundbarKeepAwake'
$startupDir = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupDir 'Soundbar Keep Awake.lnk'
$sourceDir = $PSScriptRoot

if (-not (Test-Path -LiteralPath $startupDir -PathType Container)) {
    throw "Startup folder was not found: $startupDir"
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceDir 'Soundbar Keep Awake.ps1') -Destination $installDir -Force
Copy-Item -LiteralPath (Join-Path $sourceDir 'Launch Soundbar Keep Awake.cmd') -Destination $installDir -Force
if (-not (Test-Path -LiteralPath (Join-Path $installDir 'settings.json'))) {
    Copy-Item -LiteralPath (Join-Path $sourceDir 'settings.json') -Destination $installDir
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shortcut.Arguments = '-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $installDir 'Soundbar Keep Awake.ps1') + '" -AutoStart'
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = 'Keep the Windows default audio output active'
$shortcut.WindowStyle = 7
$shortcut.Save()

Start-Process -FilePath $shortcut.TargetPath -ArgumentList $shortcut.Arguments -WindowStyle Hidden
Write-Output "Installed to: $installDir"
Write-Output "Starts at sign-in via: $shortcutPath"
