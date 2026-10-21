# Making the portable build on Linux (BuyTheBoat)

Produces a single, self-contained copy of BuyTheBoat that runs on a 64-bit Linux desktop
with nothing installed. It ships **empty** — the app makes its own blank database on first
launch — and keeps all its data inside its own folder. For Windows, see `BUILDING-windows.md`
beside this file.

## Quick steps

1. **Build it (from the repo root),** to confirm it compiles cleanly:
   ```
   dotnet build BuyTheBoat/BuyTheBoat.slnx
   ```
2. **Make the portable copy:**
   ```
   bash BuyTheBoat/build/make-portable.sh
   ```
3. **Done.** The ready-to-share copy is `dist/BuyTheBoat-<version>-linux-x64/` at the repo root, for
   example `dist/BuyTheBoat-0.9-linux-x64/`. Run `./BuyTheBoat`
   inside it, or double-click it in a file manager. To hand it off, copy or archive that whole
   folder (add `--archive` to step 2 for a ready-made `.tar.gz` beside it).

Step 2 does everything — builds the single executable, creates the empty
`data/ logs/ backups/ exports/` folders, and drops the `portable.marker` file.

## What you need

- **A 64-bit Linux desktop** (Intel/AMD, or ARM with `--runtime linux-arm64`). Any common
  desktop — GNOME, KDE and the like, on X11 or Wayland — works; the app opens its own window.
- **The .NET 10 SDK.** It has to be version 10; an older SDK (8 or 9) can't build this project.
- **An internet connection the first time.** The first build downloads the project's packages,
  and the first portable build downloads the .NET runtime it packs into the executable.
- **bash, tar** for the script — already on every desktop distribution.
- **Optional:** Git, to clone the repo.

## Steps

1. **Install the .NET 10 SDK.** Microsoft's instructions for each distribution are at
   <https://learn.microsoft.com/dotnet/core/install/linux>. Many distributions carry it as a
   package, usually named `dotnet-sdk-10.0` (for example `sudo apt install dotnet-sdk-10.0` or
   `sudo dnf install dotnet-sdk-10.0`); where yours doesn't, the same page has an install script.
   To check it worked, open a **new** terminal and run:
   ```
   dotnet --list-sdks
   ```
   It prints one line per .NET SDK installed. You're set if one of them starts with `10.`.
   If it says `dotnet: command not found`, the SDK isn't installed (or isn't on your `PATH`) yet.
2. **Get the code** — `git clone` the repo, or download it as a zip and unzip it.
3. **Build it** and **make the portable copy** — Quick steps 1 and 2 above.

Other handy commands, also from the repo root:

| Command | What it does |
| --- | --- |
| `dotnet run --project BuyTheBoat/src/BuyTheBoat.Desktop` | Runs the app straight from source. Uses your home folder's `~/.local/share/BuyTheBoat/`, not a portable one. |
| `dotnet test BuyTheBoat/BuyTheBoat.slnx` | Runs all the tests. |

---

## Details

### What the script does

`build/make-portable.sh`:

1. Reads the app's version (see "The version number" below) and names the build
   `BuyTheBoat-<version>-<runtime>`, for example `BuyTheBoat-0.9-linux-x64`.
2. Creates `dist/` at the repo root if it isn't there, and deletes any previous copy of this same
   version and runtime inside it so each run is clean. Other builds in `dist/` are left alone.
3. Runs `dotnet publish` on the app as **one self-contained executable** (see the command below)
   and marks it runnable, then deletes the graphics libraries' loose debug-symbol (`.pdb`) files,
   which the app never reads.
4. Creates the labeled subfolders `data/ logs/ backups/ exports/` and writes `portable.marker`.
5. Writes a short `README.txt` for whoever runs the copy.
6. (Optional, `--archive`) packs the folder into `dist/BuyTheBoat-<version>-<runtime>.tar.gz`. A `.tar.gz` rather than
   a `.zip`, because it keeps the executable marked as runnable.

### The version number

The app's version is set in one place: `<Version>` in `src/BuyTheBoat.Desktop/BuyTheBoat.Desktop.csproj`
(currently `0.9`). The script reads it each time it runs, so changing that one line is all it takes
for the next build to be named for the new version.

### The publish command, explained

The script runs this:

```
dotnet publish BuyTheBoat/src/BuyTheBoat.Desktop \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=embedded \
    -o dist/BuyTheBoat-0.9-linux-x64
```

| Part | What it does |
| --- | --- |
| `-c Release` | Optimized build, no debug scaffolding. |
| `-r linux-x64` | The machine the copy will run on: 64-bit Linux on Intel/AMD. |
| `--self-contained true` | Bundles the .NET runtime in, so the target machine needs nothing installed. |
| `-p:PublishSingleFile=true` | Collapses the app + its libraries into one executable. |
| `-p:IncludeNativeLibrariesForSelfExtract=true` | Pulls the native pieces (SQLite, Skia graphics) into the executable too, so no loose `.so` files sit beside it. |
| `-p:DebugType=embedded` | Keeps debug symbols inside the executable (good error-log stack traces, no loose `.pdb`). |
| `-o …/dist/BuyTheBoat-0.9-linux-x64` | Where the build lands (the script fills in the real version and runtime). |

The executable is large because the whole runtime is inside it — that's the trade for
"one file, runs anywhere."

### Changing how it works

- **Target an ARM machine** (Raspberry Pi 4/5 with a 64-bit OS, ARM laptops):
  ```
  bash BuyTheBoat/build/make-portable.sh --runtime linux-arm64
  ```
- **Also make an archive:** add `--archive` → produces `dist/BuyTheBoat-<version>-<runtime>.tar.gz` beside the folder.
- **Smaller executable** (slower first launch): add `-p:EnableCompressionInSingleFile=true` to the
  `dotnet publish` line in the script.

### Portable mode — how "only the demo does this" works

The app decides where to keep its files in `AppPaths` (`src/BuyTheBoat.Persistence/AppPaths.cs`):

- If a file named **`portable.marker`** sits next to the running executable, it's the portable
  copy → it uses `data/ logs/ backups/ exports/` next to the executable.
- Otherwise (a `dotnet run` build) → it uses `~/.local/share/BuyTheBoat/` in your home folder
  (or `$XDG_DATA_HOME/BuyTheBoat/` if you've set that).

So the packaging script's one special act is dropping that marker. Nothing else, and no build
flavor, distinguishes the two.

### Where the portable copy keeps things

Inside `dist/BuyTheBoat-<version>-linux-x64/`:

- `data/` — the database (created on first launch; empty until then).
- `backups/` — a timestamped copy saved automatically before each **Import Data**.
- `exports/` — the default location for **Export Data** and **Export as Spreadsheet**.
- `logs/errors.log` — written only if something goes wrong.

### If it won't start

- **"Permission denied"** when running `./BuyTheBoat`: the file lost its runnable mark (for
  example, it was copied through a zip). Run `chmod +x BuyTheBoat` in the folder.
- **Double-clicking opens it as text or does nothing:** turn on "Allow executing file as program"
  in the file's Properties > Permissions, or run it from a terminal.
- **It needs a desktop session:** it won't open over a plain SSH login or a text-only console.

### Housekeeping

- Each build in `dist/` is a large build output, regenerated on every run, so `dist/` is kept
  out of git: the repo's `.gitignore` excludes the whole folder.
- Building needs the **.NET 10 SDK**; see "What you need" above.
