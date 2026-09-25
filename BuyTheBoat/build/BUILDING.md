# Making the portable build (BuyTheBoat)

Produces a single, self-contained copy of BuyTheBoat that runs on any 64-bit
Windows 10 or 11 PC with nothing installed. It ships **empty** — the app makes its own blank
database on first launch — and keeps all its data inside its own folder.

## Quick steps

1. **In Visual Studio:** open `BuyTheBoat\BuyTheBoat.slnx` and build
   (Ctrl+Shift+B) to confirm it compiles cleanly.
2. **In a terminal (from the repo root):**
   ```
   powershell -ExecutionPolicy Bypass -File BuyTheBoat\build\make-portable.ps1
   ```
3. **Done.** The ready-to-share copy is `BuyTheBoatDemo\` (at the repo root). Double-click
   `BuyTheBoat.App.exe` inside it to run. To hand it off, copy or zip that whole folder.

That's it. Step 2 does everything — builds the single .exe, creates the empty
`data\ logs\ backups\ exports\` folders, and drops the `portable.marker` file. There is
no separate folder-setup script to run.

## Building without Visual Studio

Visual Studio is optional. Everything above runs on the free **.NET 10 SDK** alone —
step 2's script never used Visual Studio in the first place.

### What you need

- **Windows 10 or 11.** Sorry, I haven't started porting this to Mac or Linux yet. 
	Windows 7 and 8.1 won't work either since .NET dropped them.
- **The .NET 10 SDK** — step 1 below. It has to be version 10; an older SDK (8 or 9)
  can't build this project.
- **An internet connection the first time.** The first build downloads the project's
  packages, and the first portable build downloads the .NET runtime it packs into the exe.
- **Windows PowerShell 5.1** for the portable script — already built into Windows 10 and 11.
- **Optional:** Git, to clone the repo. **Visual Studio 2026**, if you want to edit this program for yourself

### Steps

1. **Install the .NET 10 SDK** from <https://dotnet.microsoft.com/download/dotnet/10.0>.
   Pick the **SDK**, not just the Runtime. To check it worked, open a **new** terminal
   (one opened before the install won't find it) and run:
   ```
   dotnet --list-sdks
   ```
   It prints one line per .NET SDK installed. You're set if one of them starts with `10.`,
   like this:
   ```
   10.0.401 [C:\Program Files\dotnet\sdk]
   ```
   If it says `'dotnet' is not recognized`, the SDK isn't installed yet.
2. **Get the code** — `git clone` the repo, or download it as a zip and unzip it.
3. **Build it** (from the repo root). This replaces Quick step 1's Ctrl+Shift+B:
   ```
   dotnet build BuyTheBoat\BuyTheBoat.slnx
   ```
4. **Make the portable copy** — same command as Quick step 2:
   ```
   powershell -ExecutionPolicy Bypass -File BuyTheBoat\build\make-portable.ps1
   ```

Other handy commands, also from the repo root:

| Command | What it does |
| --- | --- |
| `dotnet run --project BuyTheBoat\src\BuyTheBoat.App` | Runs the app straight from source, like F5 in Visual Studio. Uses the hidden AppData folder, not a portable one. |
| `dotnet test BuyTheBoat\BuyTheBoat.slnx` | Runs all the tests. |

---

## Details

### What the script does

`build\make-portable.ps1`:

1. Deletes any previous `BuyTheBoatDemo\` (at the repo root) so each run is clean.
2. Runs `dotnet publish` on the app as **one self-contained .exe** (see the command below).
3. Creates the labeled subfolders `data\ logs\ backups\ exports\` and writes `portable.marker`.
4. Writes a short `README.txt` for whoever runs the copy.
5. (Optional) zips the folder.

### Visual Studio vs. the script

Building in Visual Studio (Ctrl+Shift+B, or F5 to run) is for **development** — it produces
the normal loose-DLL build under `bin\`, and a run from there uses the hidden Windows AppData
folder, *not* a portable folder. That's on purpose (see "Portable mode" below), so developing
in VS never touches a portable copy's data.

The **portable** build needs `dotnet publish` with self-contained + single-file options that a
plain VS build doesn't apply, which is why it's a script. (Visual Studio *can* do this through a
Publish profile, but the script keeps it one repeatable command.)

### The publish command, explained

The script runs this (line-broken here for readability):

```
dotnet publish BuyTheBoat\src\BuyTheBoat.App ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=embedded ^
    -o BuyTheBoatDemo
```

| Part | What it does |
| --- | --- |
| `-c Release` | Optimized build, no debug scaffolding. |
| `-r win-x64` | The machine the copy will run on: 64-bit Windows on Intel/AMD. |
| `--self-contained true` | Bundles the .NET runtime in, so the target PC needs nothing installed. |
| `-p:PublishSingleFile=true` | Collapses the app + its DLLs into one `.exe`. |
| `-p:IncludeNativeLibrariesForSelfExtract=true` | Pulls the native pieces (SQLite, WPF) into the exe too, so no loose DLLs sit beside it. |
| `-p:DebugType=embedded` | Keeps debug symbols inside the exe (good error-log stack traces, no loose `.pdb`). |
| `-o …\BuyTheBoatDemo` | Where the build lands. |

The exe is large (~150 MB) because the whole runtime is inside it — that's the trade for
"one file, runs anywhere."

### Changing how it works

- **Target an ARM PC** (Surface, etc.): pass `-Runtime win-arm64`:
  ```
  powershell -ExecutionPolicy Bypass -File BuyTheBoat\build\make-portable.ps1 -Runtime win-arm64
  ```
- **Also make a zip:** add `-Zip` → produces `BuyTheBoatDemo.zip` (at the repo root).
- **Smaller exe** (slower first launch): add `-p:EnableCompressionInSingleFile=true` to the
  `dotnet publish` line in the script.
- **Loose-DLL folder instead of one exe** (not recommended — it's the messy pile of DLLs):
  remove the three `-p:...` lines from the `dotnet publish` command.

### Portable mode — how "only the demo does this" works

The app decides where to keep its files in `AppPaths` (`src\BuyTheBoat.Persistence\AppPaths.cs`):

- If a file named **`portable.marker`** sits next to the running `.exe`, it's the portable
  copy → it uses `data\ logs\ backups\ exports\` next to the exe.
- Otherwise (a normal VS/`dotnet run` build) → it uses the hidden
  `%LocalAppData%\BuyTheBoat\` folder, exactly as before.

So the packaging script's one special act is dropping that marker. Nothing else, and no build
flavor, distinguishes the two.

### Where the portable copy keeps things

Inside `BuyTheBoatDemo\`:

- `data\` — the database (created on first launch; empty until then).
- `backups\` — a timestamped copy saved automatically before each **Import Data**.
- `exports\` — the default location for **Export Data** and **Export as Spreadsheet**.
- `logs\errors.log` — written only if something goes wrong.

### Housekeeping

- `BuyTheBoatDemo\` (at the repo root) is a ~150 MB build output. It's regenerated on every run, so it's
  kept out of git: the repo's `.gitignore` already excludes it and the zip.
- Requires the **.NET 10 SDK** installed to build (Visual Studio 2026 covers this; Visual
  Studio 2022 can't build for .NET 10). See "What you need" above for the full list.
