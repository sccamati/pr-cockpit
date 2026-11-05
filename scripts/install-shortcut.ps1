# Builds the app into publish\ and puts a "PR Cockpit" shortcut on the desktop.
# Rerun after pulling changes or moving the repo — the shortcut runs what was built here.
#
# The shortcut starts one process: the Release backend, which also serves the built frontend.
# No dotnet run, no MSBuild, no Vite, no Node. Its console starts minimized; closing it stops
# the app. A second click while it runs only opens the browser.
#
# The icon is a stock Windows one. To use your own, drop icon.ico next to this file.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publish = "$root\publish"
$exe = "$publish\PRCockpit.Api.exe"
$custom = "$PSScriptRoot\icon.ico"
$linkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PR Cockpit.lnk'

# A running instance locks its own files, so publish would fail halfway.
Get-Process PRCockpit.Api -ErrorAction SilentlyContinue | Stop-Process -Force

Push-Location "$root\frontend"
try {
    if (-not (Test-Path node_modules)) { & npm ci; if ($LASTEXITCODE) { throw 'npm ci nie powiodlo sie.' } }
    & npm run build; if ($LASTEXITCODE) { throw 'Build frontendu nie powiodl sie.' }
} finally { Pop-Location }

& dotnet publish "$root\backend\PRCockpit.Api" -c Release -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish nie powiodl sie.' }

if (Test-Path "$publish\wwwroot") { Remove-Item "$publish\wwwroot" -Recurse -Force }
Copy-Item "$root\frontend\dist" "$publish\wwwroot" -Recurse

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($linkPath)
$link.TargetPath = $exe
# Development, because that is the environment that loads User Secrets (PAT, connection string).
$link.Arguments = '--environment Development --urls http://localhost:7180 --OpenBrowser true'
# The content root is the working directory, and wwwroot lives there.
$link.WorkingDirectory = $publish
$link.WindowStyle = 7 # minimized
$link.IconLocation = if (Test-Path $custom) { $custom } else { "$env:SystemRoot\System32\imageres.dll,109" }
$link.Description = 'Uruchamia PR Cockpit i otwiera go w przegladarce'
$link.Save()

Write-Host "Skrot gotowy: $linkPath" -ForegroundColor Green
Write-Host "Po zmianach w kodzie uruchom ten skrypt ponownie." -ForegroundColor DarkGray
