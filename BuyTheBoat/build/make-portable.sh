#!/usr/bin/env bash
#
#  make-portable.sh
#
#  Builds the self-contained Linux portable copy of BuyTheBoat into
#  dist/BuyTheBoat-<version>-<runtime>/ at the repository root, e.g. dist/BuyTheBoat-0.9-linux-x64/.
#  The version is read from BuyTheBoat.Desktop.csproj, and dist/ is created if it isn't there.
#  (For Windows, see make-portable.ps1.) It publishes as a
#  SINGLE bulky executable: the whole .NET runtime is bundled inside the one file,
#  so there's no loose pile of libraries to keep together and nothing to install
#  on the machine that runs it. The folder ends up holding:
#    - BuyTheBoat (the one big self-contained executable),
#    - the labeled data/ logs/ backups/ exports/ subfolders, and
#    - portable.marker, the file that tells the app to keep its data here.
#
#  It ships EMPTY on purpose: the app creates a fresh, empty database in data/ the
#  first time it's run. Re-run this any time to produce a fresh copy from the
#  current source; hand off (or keep) the whole folder.
#
#  Usage:
#    bash BuyTheBoat/build/make-portable.sh
#  Options:
#    --runtime linux-x64   Target the copy will run on (default linux-x64; linux-arm64 for ARM).
#    --archive             Also produce dist/BuyTheBoat-<version>-<runtime>.tar.gz beside the folder.
#
set -euo pipefail

runtime="linux-x64"
archive=false
while [[ $# -gt 0 ]]; do
    case "$1" in
        --runtime) runtime="$2"; shift 2 ;;
        --archive) archive=true; shift ;;
        *) echo "Unknown option: $1 (use --runtime <rid> or --archive)" >&2; exit 1 ;;
    esac
done

# --- Locate everything relative to this script -----------------------------
build_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"   # .../BuyTheBoat/BuyTheBoat/build
project_root="$(dirname "$build_dir")"                      # .../BuyTheBoat/BuyTheBoat
repo_root="$(dirname "$project_root")"                      # .../BuyTheBoat (repository root)
app_project="$project_root/src/BuyTheBoat.Desktop/BuyTheBoat.Desktop.csproj"
dist_dir="$repo_root/dist"

# --- Name the output after the app's version and the target ----------------
version="$(dotnet msbuild "$app_project" -getProperty:Version | tr -d '[:space:]')"
if [[ -z "$version" ]]; then
    echo "Couldn't read the app's version from $app_project." >&2
    exit 1
fi
build_name="BuyTheBoat-$version-$runtime"
demo_dir="$dist_dir/$build_name"

echo "Portable build target: $demo_dir"

# --- Fresh output folder ---------------------------------------------------
# Only this version and target's folder is replaced; other builds in dist/ stay.
mkdir -p "$dist_dir"
if [[ -d "$demo_dir" ]]; then
    echo "Removing previous build folder..."
    rm -rf "$demo_dir"
fi

# --- Publish the app as a single self-contained executable -----------------
# PublishSingleFile           collapses the app + its libraries into one executable.
# IncludeNativeLibraries...    pulls the native pieces (SQLite, Skia graphics) into it too,
#                             so they don't sit loose beside the executable.
# DebugType=embedded          keeps the debug symbols inside the executable (good error
#                             logs, no loose .pdb) instead of a separate file.
echo "Publishing single-file $runtime self-contained build (first run downloads the runtime)..."
dotnet publish "$app_project" -c Release -r "$runtime" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=embedded \
    -o "$demo_dir"
chmod +x "$demo_dir/BuyTheBoat"

# The graphics libraries (SkiaSharp, HarfBuzzSharp) ship debug-symbol files that land loose beside the executable (~100 MB); the app never reads them.
rm -f "$demo_dir"/*.pdb

# --- Labeled subfolders + the portable marker ------------------------------
# data/ is left empty: the app creates the database there on first launch.
mkdir -p "$demo_dir/data" "$demo_dir/logs" "$demo_dir/backups" "$demo_dir/exports"
echo "This file tells BuyTheBoat to run in portable mode, keeping its data in this folder. Leave it here." > "$demo_dir/portable.marker"

# --- A short note for whoever runs it --------------------------------------
cat > "$demo_dir/README.txt" <<'EOF'
Buy The Boat Demo - portable (Linux)
====================================

How to run this:
  Double click the BuyTheBoat file, or open a terminal in this folder and run:
    ./BuyTheBoat
  You don't have to install anything.
  If double clicking opens it as text or does nothing, your file manager may need
  "Allow executing file as program" turned on for it (right click > Properties > Permissions).

What are all these folders?:
  This demo of the program is self contained inside this folder.
  You can rename the outer folder all you want, just don't mess with the inner ones.
  Everything this copy creates stays inside this one folder:
    data/      this contains your database. It's created the first time you run the program.
    backups/   this is a copy automatically created before you try using "Import Data"
    exports/   this is where "Export Data" and "Export as Spreadsheet" get saved
    logs/      error logs. I might ask you to send me stuff from here if you have problems.

Where should I put this?
  You can copy this entire folder anywhere you can write to, like your home folder.
  You can even run it on a USB stick.
  Don't put it somewhere you need sudo to write, like /opt or /usr.
  Just put it somewhere convenient.

So how do I actually use this program to manage my budget?
  By the grace of God and human curiosity. Good luck!
EOF

# --- Optional archive ------------------------------------------------------
# A .tar.gz rather than a .zip, because it keeps BuyTheBoat marked as runnable.
if [[ "$archive" == true ]]; then
    archive_path="$dist_dir/$build_name.tar.gz"
    rm -f "$archive_path"
    echo "Archiving to $archive_path ..."
    tar -czf "$archive_path" -C "$dist_dir" "$build_name"
fi

echo ""
echo "Done. Portable build is at: $demo_dir"
echo "One self-contained executable; opens empty (database created on first launch); runs on 64-bit Linux desktops with nothing pre-installed."
