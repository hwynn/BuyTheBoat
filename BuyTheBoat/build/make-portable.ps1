#Requires -Version 5.1
<#
  make-portable.ps1

  Builds the self-contained "BuyTheBoat" portable copy of MyMoneyForecast into
  redesign\BuyTheBoatDemo. It publishes as a SINGLE bulky .exe — the whole .NET
  runtime is bundled inside the one executable, so there's no loose pile of DLLs
  to keep together and nothing to install on the machine that runs it. The
  folder ends up holding:
    - MyMoneyForecast.App.exe (the one big self-contained executable),
    - the labeled data\ / logs\ / backups\ / exports\ subfolders, and
    - portable.marker, the file that tells the app to keep its data here.

  It ships EMPTY on purpose: the app creates a fresh, empty database in data\ the
  first time it's run. Re-run this any time to produce a fresh copy from the
  current source; hand off (or keep) the whole BuyTheBoatDemo folder.

  Usage:
    powershell -ExecutionPolicy Bypass -File make-portable.ps1
  Options:
    -Runtime win-x64   Target the copy will run on (default win-x64).
    -Zip               Also produce BuyTheBoatDemo.zip beside the folder.
#>
[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

# --- Locate everything relative to this script -----------------------------
$buildDir     = $PSScriptRoot                                    # ...\redesign\MyMoneyForecast\build
$projectRoot  = Split-Path $buildDir -Parent                    # ...\redesign\MyMoneyForecast
$redesignRoot = Split-Path $projectRoot -Parent                 # ...\redesign
$appProject   = Join-Path $projectRoot "src\MyMoneyForecast.App\MyMoneyForecast.App.csproj"
$demoDir      = Join-Path $redesignRoot "BuyTheBoatDemo"

Write-Host "Portable build target: $demoDir"

# --- Fresh output folder ---------------------------------------------------
if (Test-Path $demoDir) {
    Write-Host "Removing previous build folder..."
    Remove-Item $demoDir -Recurse -Force
}

# --- Publish the app as a single self-contained .exe -----------------------
# PublishSingleFile           collapses the app + its DLLs into one executable.
# IncludeNativeLibraries...    pulls the native pieces (SQLite, WPF) into it too,
#                             so they don't sit loose beside the exe.
# DebugType=embedded          keeps the debug symbols inside the exe (good error
#                             logs, no loose .pdb) instead of a separate file.
Write-Host "Publishing single-file $Runtime self-contained build (first run downloads the runtime)..."
dotnet publish $appProject -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -o $demoDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

# --- Labeled subfolders + the portable marker ------------------------------
# data\ is left empty: the app creates the database there on first launch.
foreach ($sub in "data", "logs", "backups", "exports") {
    New-Item -ItemType Directory -Path (Join-Path $demoDir $sub) -Force | Out-Null
}
$markerText = "This file tells MyMoneyForecast to run in portable mode, keeping its data in this folder. Leave it here."
Set-Content -Path (Join-Path $demoDir "portable.marker") -Value $markerText -Encoding utf8

# --- A short note for whoever runs it --------------------------------------
$readme = @'
MyMoneyForecast - portable
==========================

TO RUN
  Double-click MyMoneyForecast.App.exe. Nothing to install.

WHERE YOUR DATA LIVES
  Everything this copy creates stays inside this one folder:
    data\      your database (created the first time you run it)
    backups\   a copy saved automatically before each "Import Data"
    exports\   where "Export Data" and "Export as Spreadsheet" save by default
    logs\      errors.log - written only if something goes wrong

MOVING OR COPYING IT
  Copy this whole folder anywhere - another PC, a USB stick - and it keeps
  working, data and all. Keep it somewhere you can write to (Desktop, Downloads,
  a USB drive), not inside Program Files.
'@
Set-Content -Path (Join-Path $demoDir "README.txt") -Value $readme -Encoding utf8

# --- Optional zip ----------------------------------------------------------
if ($Zip) {
    $zipPath = Join-Path $redesignRoot "BuyTheBoatDemo.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host "Zipping to $zipPath ..."
    Compress-Archive -Path $demoDir -DestinationPath $zipPath
}

Write-Host ""
Write-Host "Done. Portable build is at: $demoDir"
Write-Host "One self-contained .exe; opens empty (database created on first launch); runs on any 64-bit Windows PC with nothing pre-installed."
