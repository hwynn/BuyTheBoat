namespace BuyTheBoat.Persistence;

// The single place that decides WHERE the app keeps its files. Two layouts:
//
//   Normal (a developer machine, the SeedData tool, the test host): one hidden
//   folder, %LocalAppData%\BuyTheBoat, holding the database and error log
//   directly — exactly the layout the app has always used.
//
//   Portable demo, and ONLY it: labeled subfolders (data\, logs\, backups\,
//   exports\) next to the app's own .exe, so the whole demo is one folder you
//   can copy to another machine or a USB stick and it keeps working, data and
//   all.
//
// The two are told apart by a marker file the packaging script drops beside the
// demo .exe (PortableMarkerFileName) and nothing else has. Which layout we're
// in is decided ONCE, from the running process's own .exe — so the SeedData
// tool and the tests, whose .exe has no marker beside it, keep resolving to the
// LocalAppData layout even though they share this same code.
public static class AppPaths
{
    // Dropped beside the demo .exe by the packaging script; its presence there
    // is the entire signal that this run is the portable demo.
    public const string PortableMarkerFileName = "portable.marker";

    private const string ProductFolderName = "BuyTheBoat";
    private const string DatabaseFileName = "buytheboat.db";
    private const string LogFileName = "errors.log";

    // Decided once per run (the marker can't meaningfully appear or vanish
    // mid-session): the demo folder when the marker sits beside the running
    // .exe, otherwise null for the normal LocalAppData layout.
    private static readonly string? PortableFolder = DetectPortableFolder();

    /// <summary>[CALC] Whether this run is the portable demo, keeping its files beside its own .exe. False for developer runs, the SeedData tool, and tests, which all use the hidden LocalAppData folder.</summary>
    public static bool IsPortable => PortableFolder is not null;

    /// <summary>[CALC] Full path to the live database file — under the demo's data\ folder when portable, else directly in %LocalAppData%\BuyTheBoat as the app has always stored it.</summary>
    public static string DatabasePath => PortableFolder is { } demo
        ? Path.Combine(demo, "data", DatabaseFileName)
        : Path.Combine(LocalAppDataFolder, DatabaseFileName);

    /// <summary>[CALC] Full path to the error log — the demo's logs\ folder when portable, else alongside the database in the LocalAppData folder as before.</summary>
    public static string LogFilePath => PortableFolder is { } demo
        ? Path.Combine(demo, "logs", LogFileName)
        : Path.Combine(LocalAppDataFolder, LogFileName);

    /// <summary>[CALC] Folder the Export dialogs open to by default in the demo (its exports\ folder), or null off-demo so the dialog keeps its own default location.</summary>
    public static string? DefaultExportFolder => PortableFolder is { } demo
        ? Path.Combine(demo, "exports")
        : null;

    /// <summary>[CALC] Where Import should save the current database before overwriting it: a timestamped copy in the demo's backups\ folder (so older backups are kept), or the single "&lt;db&gt;.bak" beside the database off-demo, matching the app's original behavior.</summary>
    public static string NextImportBackupPath() => PortableFolder is { } demo
        ? Path.Combine(demo, "backups", $"{Path.GetFileNameWithoutExtension(DatabaseFileName)}-{DateTime.Now:yyyy-MM-dd-HHmmss}.db")
        : DatabasePath + ".bak";

    /// <summary>[WRITES FILE] Creates the demo's data\, logs\, backups\, and exports\ subfolders if they're missing, so they're visible and ready the first time the demo runs. Does nothing off-demo. Call once at startup.</summary>
    public static void EnsurePortableFoldersExist()
    {
        if (PortableFolder is not { } demo)
        {
            return;
        }

        foreach (var subfolder in new[] { "data", "logs", "backups", "exports" })
        {
            Directory.CreateDirectory(Path.Combine(demo, subfolder));
        }
    }

    // %LocalAppData%\BuyTheBoat — the off-demo home, unchanged from the
    // app's original single-folder layout.
    private static string LocalAppDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductFolderName);

    /// <summary>[READS FILE] Looks beside the running .exe for the portable marker file, returning that folder when it's there and null otherwise. The one disk check behind the whole portable-vs-normal decision — run once and cached.</summary>
    private static string? DetectPortableFolder()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return null;
        }

        var exeFolder = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(exeFolder))
        {
            return null;
        }

        return File.Exists(Path.Combine(exeFolder, PortableMarkerFileName)) ? exeFolder : null;
    }
}
