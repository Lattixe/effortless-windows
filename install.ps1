# Effortless Windows Installer Script
# Run with: powershell -ExecutionPolicy Bypass -File install.ps1

$ErrorActionPreference = "Stop"

Write-Host "Installing Effortless..." -ForegroundColor Cyan

# Paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishDir = Join-Path $ScriptDir "publish"
$InstallDir = Join-Path $env:LOCALAPPDATA "Effortless"
$ExePath = Join-Path $InstallDir "Effortless.exe"
$StartMenuPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Effortless.lnk"
$StartupPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\Effortless.lnk"
$DesktopPath = Join-Path ([Environment]::GetFolderPath('Desktop')) "Effortless.lnk"

# Check if publish folder exists
if (-not (Test-Path $PublishDir)) {
    Write-Host "Error: publish folder not found. Run 'dotnet publish -c Release -r win-x64 --self-contained -o publish' first." -ForegroundColor Red
    exit 1
}

# Create install directory
Write-Host "Creating install directory..." -ForegroundColor Gray
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# Copy files
Write-Host "Copying files..." -ForegroundColor Gray
Copy-Item -Path "$PublishDir\*" -Destination $InstallDir -Recurse -Force

# Create shortcut function
function Create-Shortcut {
    param (
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$Description
    )

    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = $TargetPath
    $Shortcut.WorkingDirectory = Split-Path $TargetPath
    $Shortcut.Description = $Description
    # Use the app's embedded icon (from app.ico via <ApplicationIcon>)
    $Shortcut.IconLocation = "$TargetPath,0"
    $Shortcut.Save()
}

# Create Start Menu shortcut
Write-Host "Creating Start Menu shortcut..." -ForegroundColor Gray
Create-Shortcut -ShortcutPath $StartMenuPath -TargetPath $ExePath -Description "Effortless - Minimalist Task Timer"

# Create Desktop shortcut (double-click icon to launch)
Write-Host "Creating Desktop shortcut..." -ForegroundColor Gray
Create-Shortcut -ShortcutPath $DesktopPath -TargetPath $ExePath -Description "Effortless - Minimalist Task Timer"

# Create Startup shortcut (run on startup)
Write-Host "Adding to Windows startup..." -ForegroundColor Gray
Create-Shortcut -ShortcutPath $StartupPath -TargetPath $ExePath -Description "Effortless - Minimalist Task Timer"

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Effortless has been installed to: $InstallDir" -ForegroundColor White
Write-Host "Desktop shortcut created - double-click 'Effortless' on your desktop" -ForegroundColor White
Write-Host "Start Menu shortcut created - search for 'Effortless' in Start" -ForegroundColor White
Write-Host "Startup shortcut created - Effortless will run on Windows startup" -ForegroundColor White
Write-Host ""
Write-Host "To update to the latest version later, re-run install-latest.ps1." -ForegroundColor Gray
Write-Host ""
Write-Host "Starting Effortless now..." -ForegroundColor Cyan

# Start the application
Start-Process $ExePath
