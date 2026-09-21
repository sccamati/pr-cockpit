# Starts the whole application from one click: backend, frontend, browser.
#
# Both children run in this window, so their logs are here and closing the window is the
# stop button. Windows does not kill them when the window goes, so the script begins by
# clearing whatever is still holding the two ports — a launcher that cannot be launched
# twice is worse than one that cleans up after itself.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$backendPort = 5164
$frontendPort = 5173
$appUrl = "http://localhost:$frontendPort"
$children = @()

function Stop-Tree([int]$processId) {
    if (-not $processId) { return }
    taskkill /PID $processId /T /F 2>&1 | Out-Null
}

# Whatever is listening there is a leftover of a previous run, or something else entirely.
# Either way the app cannot start on top of it, so it goes before anything else is tried.
function Clear-Port([int]$port, [string]$label) {
    $owners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    foreach ($owner in $owners) {
        Write-Host "Port $port ($label) jest zajety przez proces $owner - zamykam go." -ForegroundColor Yellow
        Stop-Tree $owner
    }
}

function Require-Command([string]$name, [string]$hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Nie znalazlem '$name' w PATH. $hint"
    }
}

try {
    Write-Host "PR Cockpit - start" -ForegroundColor Cyan
    Write-Host $root -ForegroundColor DarkGray

    Require-Command 'dotnet' 'Zainstaluj .NET SDK.'
    Require-Command 'npm' 'Zainstaluj Node.js.'

    Clear-Port $backendPort 'backend'
    Clear-Port $frontendPort 'frontend'

    # First run after a clone has no node_modules, and vite then fails with a message that
    # does not say what to do about it.
    if (-not (Test-Path "$root\frontend\node_modules")) {
        Write-Host "Brak node_modules - instaluje zaleznosci (to potrwa)." -ForegroundColor Yellow
        Push-Location "$root\frontend"
        try { & npm ci } finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw "npm ci nie powiodlo sie." }
    }

    Write-Host "Startuje backend na porcie $backendPort..." -ForegroundColor Cyan
    $children += Start-Process -FilePath 'dotnet' -PassThru -NoNewWindow -ArgumentList @(
        'run', '--project', "$root\backend\PRCockpit.Api", '--launch-profile', 'http')

    # Through cmd, because in PowerShell "npm" resolves to npm.ps1, which Start-Process
    # cannot execute ("nie jest prawidlowa aplikacja systemu Win32"). cmd finds npm.cmd
    # itself, and taskkill /T takes the whole tree down with it.
    Write-Host "Startuje frontend na porcie $frontendPort..." -ForegroundColor Cyan
    $children += Start-Process -FilePath $env:ComSpec -PassThru -NoNewWindow `
        -WorkingDirectory "$root\frontend" -ArgumentList @('/c', 'npm run dev')

    # Opening the browser before vite is listening shows a connection error the user then
    # has to reload past, so the wait is the whole point of this loop.
    Write-Host "Czekam, az frontend odpowie..." -ForegroundColor DarkGray
    $ready = $false
    foreach ($attempt in 1..60) {
        if ($children | Where-Object { $_.HasExited }) {
            throw "Jeden z procesow zakonczyl sie w trakcie startu - komunikat jest wyzej w tym oknie."
        }
        if (Get-NetTCPConnection -LocalPort $frontendPort -State Listen -ErrorAction SilentlyContinue) {
            $ready = $true
            break
        }
        Start-Sleep -Seconds 1
    }

    if ($ready) {
        Start-Process $appUrl
        Write-Host ""
        Write-Host "Gotowe: $appUrl" -ForegroundColor Green
    } else {
        Write-Host "Frontend nie wstal w ciagu 60 s. Logi sa wyzej." -ForegroundColor Yellow
    }

    Write-Host "Zamkniecie tego okna albo Ctrl+C zatrzymuje aplikacje." -ForegroundColor DarkGray
    Write-Host ""

    # Backend needs a local SQL Server and the secrets from README; if it dies, this is
    # where it becomes visible instead of the UI simply failing every request.
    while ($true) {
        Start-Sleep -Seconds 2
        $dead = $children | Where-Object { $_.HasExited }
        if ($dead) {
            Write-Host "Proces $($dead[0].Id) zakonczyl sie (kod $($dead[0].ExitCode)). Zatrzymuje reszte." -ForegroundColor Yellow
            break
        }
    }
} catch {
    Write-Host ""
    Write-Host "Nie udalo sie wystartowac: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    foreach ($child in $children) { Stop-Tree $child.Id }
    Write-Host ""
    Read-Host "Nacisnij Enter, zeby zamknac"
}
