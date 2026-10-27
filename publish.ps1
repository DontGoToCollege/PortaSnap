# Publishes PortaSnap as a self-contained, single-file x64 executable.
# Usage:  .\publish.ps1            -> dist\PortaSnap.exe
#         .\publish.ps1 -Run       -> publish and launch
# Copy dist\PortaSnap.exe anywhere (USB stick, synced folder); it keeps its data in a
# PortaSnap-Data folder next to itself.
param([switch]$Run)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "src\PortaSnap\PortaSnap.csproj"
$out  = Join-Path $root "dist"

Get-Process PortaSnap -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$root*" } | Stop-Process -Force

Write-Host "Publishing PortaSnap (Release, win-x64, self-contained, single file)..." -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Only the exe is needed; drop symbol/side files so the folder stays clean.
Get-ChildItem $out -File | Where-Object { $_.Name -ne "PortaSnap.exe" } | Remove-Item -Force

$exe = Join-Path $out "PortaSnap.exe"
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Done: $exe ($size MB)" -ForegroundColor Green
if ($Run) { Start-Process $exe }
