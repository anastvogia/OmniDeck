# build.ps1
# Script to build both minimal and bundled versions of OmniDeck

$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "OmniDeck\OmniDeck.csproj"
$PublishDir = Join-Path $ProjectDir "publish"
$MinimalDir = Join-Path $PublishDir "minimal"
$BundledDir = Join-Path $PublishDir "bundled"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Starting OmniDeck Build Pipeline" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 1. Stop running instances of OmniDeck to avoid file locks
Write-Host "`n[1/4] Checking for running instances of OmniDeck..." -ForegroundColor Yellow
$runningProcesses = Get-Process -Name "OmniDeck", "Sliders" -ErrorAction SilentlyContinue
if ($runningProcesses) {
    Write-Host "Found running OmniDeck process(es). Stopping them..." -ForegroundColor Magenta
    Stop-Process -Name "OmniDeck", "Sliders" -Force
    Start-Sleep -Seconds 1
} else {
    Write-Host "No running instances found." -ForegroundColor Green
}

# 2. Clean/Prepare Publish Directories
Write-Host "`n[2/4] Preparing publish directories..." -ForegroundColor Yellow
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $MinimalDir -Force | Out-Null
New-Item -ItemType Directory -Path $BundledDir -Force | Out-Null
Write-Host "Publish directories cleaned and created." -ForegroundColor Green

# 3. Publish Minimal Version (Framework-dependent, Single-File)
# This relies on .NET 8.0 Desktop Runtime installed on the target machine.
Write-Host "`n[3/4] Publishing Minimal Version..." -ForegroundColor Yellow
dotnet publish $ProjectFile `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $MinimalDir `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -eq 0) {
    Write-Host "Minimal version published successfully to: $MinimalDir" -ForegroundColor Green
} else {
    Write-Host "Failed to build Minimal version." -ForegroundColor Red
    Exit $LASTEXITCODE
}

# 4. Publish Bundled Version (Self-contained, Single-File)
# This embeds the .NET 8.0 runtime and native WPF libraries. Runs anywhere without dependencies.
Write-Host "`n[4/4] Publishing Bundled Version..." -ForegroundColor Yellow
dotnet publish $ProjectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $BundledDir `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -eq 0) {
    Write-Host "Bundled version published successfully to: $BundledDir" -ForegroundColor Green
} else {
    Write-Host "Failed to build Bundled version." -ForegroundColor Red
    Exit $LASTEXITCODE
}

# Print Summary
Write-Host "`n=============================================" -ForegroundColor Cyan
Write-Host " Build Summary" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$minimalExe = Join-Path $MinimalDir "OmniDeck.exe"
$bundledExe = Join-Path $BundledDir "OmniDeck.exe"

if (Test-Path $minimalExe) {
    $minSize = (Get-Item $minimalExe).Length / 1MB
    Write-Host "Minimal Build (Framework-Dependent):" -ForegroundColor Green
    Write-Host "  Location: $minimalExe"
    Write-Host "  Size:     $("{0:N2}" -f $minSize) MB"
    Write-Host "  Note:     Requires .NET 8.0 Desktop Runtime to be installed on target machine."
}

if (Test-Path $bundledExe) {
    $bundledSize = (Get-Item $bundledExe).Length / 1MB
    Write-Host "`nBundled Build (Self-Contained):" -ForegroundColor Green
    Write-Host "  Location: $bundledExe"
    Write-Host "  Size:     $("{0:N2}" -f $bundledSize) MB"
    Write-Host "  Note:     Fully self-contained. Runs on any 64-bit Windows PC."
}
Write-Host "=============================================" -ForegroundColor Cyan

