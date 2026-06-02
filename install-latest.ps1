$ErrorActionPreference = "Stop"
$Branch = "claude/elegant-mccarthy-2sMgL"
$RepoUrl = "https://github.com/Lattixe/effortless-windows.git"
$DefaultClone = Join-Path $env:USERPROFILE "effortless-windows"

function Find-ProjectDir {
    if (Test-Path "Effortless.csproj") { return (Get-Location).Path }
    if (Test-Path (Join-Path $DefaultClone "Effortless.csproj")) { return $DefaultClone }
    Write-Host "Scanning $env:USERPROFILE for Effortless.csproj (this can take a moment)..." -ForegroundColor Cyan
    $hit = Get-ChildItem -Path $env:USERPROFILE -Filter Effortless.csproj -Recurse -ErrorAction SilentlyContinue -Depth 6 |
        Where-Object { $_.Directory.Name -ne "publish" -and $_.FullName -notmatch "\\bin\\|\\obj\\" } |
        Select-Object -First 1
    if ($hit) { return $hit.Directory.FullName }
    return $null
}

$ProjectDir = Find-ProjectDir
if (-not $ProjectDir) {
    Write-Host "Not found on disk. Cloning into $DefaultClone ..." -ForegroundColor Yellow
    git clone $RepoUrl $DefaultClone
    if ($LASTEXITCODE -ne 0) { throw "git clone failed" }
    $ProjectDir = $DefaultClone
}

Set-Location $ProjectDir
Write-Host "Using project at $ProjectDir" -ForegroundColor Green

Write-Host "Fetching $Branch ..." -ForegroundColor Cyan
git fetch origin $Branch
if ($LASTEXITCODE -ne 0) { throw "git fetch failed" }
git checkout $Branch
if ($LASTEXITCODE -ne 0) { throw "git checkout failed (uncommitted changes?)" }
git pull --ff-only origin $Branch
if ($LASTEXITCODE -ne 0) { throw "git pull failed (branch may have diverged)" }

$head = (git rev-parse --short HEAD).Trim()
Write-Host "On $Branch @ $head" -ForegroundColor Green

Write-Host "Stopping any running Effortless (so files can be replaced) ..." -ForegroundColor Cyan
Get-Process -Name Effortless -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$publishDir = Join-Path $ProjectDir "publish"
Write-Host "Publishing self-contained Windows build to $publishDir ..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish -c Release -r win-x64 --self-contained -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$installScript = Join-Path $ProjectDir "install.ps1"
if (-not (Test-Path $installScript)) { throw "install.ps1 not found at $installScript" }

Write-Host "Running installer (copies to LOCALAPPDATA, Start Menu, Startup, then launches) ..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File $installScript
if ($LASTEXITCODE -ne 0) { throw "install.ps1 failed" }

Write-Host ""
Write-Host "Done. Effortless is now installed and launched." -ForegroundColor Green
Write-Host "  - Closing this PowerShell will NOT close the app." -ForegroundColor Gray
Write-Host "  - Installed to:  $env:LOCALAPPDATA\Effortless\Effortless.exe" -ForegroundColor Gray
Write-Host "  - Auto-starts on Windows login (uninstall.ps1 to remove)." -ForegroundColor Gray
