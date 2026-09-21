# Puts a "PR Cockpit" shortcut on the desktop. Run once; rerun after moving the repo.
#
# The icon is a stock Windows one, because the repository has no .ico and inventing one is
# not what this script is for. To use your own, drop it next to this file as icon.ico —
# it is picked up automatically on the next run.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$start = "$PSScriptRoot\start.ps1"
$custom = "$PSScriptRoot\icon.ico"
$linkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PR Cockpit.lnk'

if (-not (Test-Path $start)) { throw "Nie znalazlem $start." }

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($linkPath)
$link.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$link.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$start`""
# The console starts in the repository, so anything the child processes print resolves to
# paths you recognise.
$link.WorkingDirectory = $root
$link.IconLocation = if (Test-Path $custom) { $custom } else { "$env:SystemRoot\System32\imageres.dll,109" }
$link.Description = 'Uruchamia backend, frontend i otwiera PR Cockpit w przegladarce'
$link.Save()

Write-Host "Skrot gotowy: $linkPath" -ForegroundColor Green
Write-Host "Ikona: $($link.IconLocation)" -ForegroundColor DarkGray
Write-Host "Mozesz go przypiac do paska zadan (prawy przycisk - Przypnij)." -ForegroundColor DarkGray
