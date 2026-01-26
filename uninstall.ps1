# Effortless Windows Uninstaller Script
# Run with: powershell -ExecutionPolicy Bypass -File uninstall.ps1

$ErrorActionPreference = "Stop"

Write-Host "Uninstalling Effortless..." -ForegroundColor Cyan

# Paths
$InstallDir = Join-Path $env:LOCALAPPDATA "Effortless"
$StartMenuPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Effortless.lnk"
$StartupPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\Effortless.lnk"

# Kill running process
Write-Host "Stopping Effortless if running..." -ForegroundColor Gray
Get-Process -Name "Effortless" -ErrorAction SilentlyContinue | Stop-Process -Force

Start-Sleep -Seconds 1

# Remove shortcuts
Write-Host "Removing shortcuts..." -ForegroundColor Gray
if (Test-Path $StartMenuPath) {
    Remove-Item $StartMenuPath -Force
}
if (Test-Path $StartupPath) {
    Remove-Item $StartupPath -Force
}

# Remove install directory (but keep data)
Write-Host "Removing application files..." -ForegroundColor Gray
$DataFolder = Join-Path $env:LOCALAPPDATA "Effortless"
$ExeFile = Join-Path $DataFolder "Effortless.exe"

if (Test-Path $ExeFile) {
    # Remove all files except tasks.json and scratchpad.txt
    Get-ChildItem $InstallDir -File | Where-Object { $_.Name -notin @("tasks.json", "scratchpad.txt") } | Remove-Item -Force
    Get-ChildItem $InstallDir -Directory | Remove-Item -Recurse -Force
}

Write-Host ""
Write-Host "Uninstallation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Note: Your tasks and scratch pad data have been preserved in:" -ForegroundColor Yellow
Write-Host "  $DataFolder" -ForegroundColor White
Write-Host "Delete this folder manually if you want to remove all data." -ForegroundColor Yellow
