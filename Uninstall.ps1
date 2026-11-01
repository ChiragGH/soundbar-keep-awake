$ErrorActionPreference = 'Stop'

$expectedDir = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\SoundbarKeepAwake'))
$allowedParent = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
if ([IO.Path]::GetDirectoryName($expectedDir) -ne $allowedParent) {
    throw 'Unexpected install path; nothing was removed.'
}

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Soundbar Keep Awake.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
if (Test-Path -LiteralPath $expectedDir) { Remove-Item -LiteralPath $expectedDir -Recurse -Force }
Write-Output 'Soundbar Keep Awake was uninstalled.'
