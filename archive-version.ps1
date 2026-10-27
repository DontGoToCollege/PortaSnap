# Freezes the current PortaSnap version (source + published exe) into ..\PortaSnap_Archive\PortaSnap_v<version>
# before starting work on the next version. The version is read from src\PortaSnap\PortaSnap.csproj.
# Usage:  .\archive-version.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "src\PortaSnap\PortaSnap.csproj"
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $proj" }

$dest = Join-Path (Split-Path -Parent $root) "PortaSnap_Archive\PortaSnap_v$version"
if (Test-Path $dest) { throw "Archive already exists: $dest (bump <Version> in the csproj first)" }

Get-Process PortaSnap -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$root*" } | Stop-Process -Force
New-Item -ItemType Directory -Force $dest | Out-Null
robocopy $root $dest /E /XD bin obj .vs PortaSnap-Data /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
Write-Host "Archived v$version -> $dest" -ForegroundColor Green
