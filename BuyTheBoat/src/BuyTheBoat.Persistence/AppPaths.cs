namespace BuyTheBoat.Persistence;

/// <summary>
/// Decides where the app keeps its files, in one of two layouts.
/// Normal (a developer machine, the SeedData tool, the test host): the hidden %LocalAppData%\BuyTheBoat folder holds the database and error log directly.
/// Portable demo only: labeled subfolders (data\, logs\, backups\, exports\) beside the app's .exe, so the whole demo is one folder that keeps working when copied to another machine or a USB stick.
/// </summary>
/// <remarks>
/// The portable demo is recognized by a marker file the packaging script puts beside the demo .exe (PortableMarkerFileName).
/// The layout is decided once, from the running process's own .exe, so the SeedData tool and the tests, which share this code but have no marker, use the LocalAppData layout.
/// </remarks>
public static class AppPaths
{
    /// <summary>The marker file the packaging script puts beside the demo .exe; its presence means this run is the portable demo.</summary>
    public const string PortableMarkerFileName = "portable.marker";

    private const string ProductFolderName = "BuyTheBoat";
    private const string DatabaseFileName = "buytheboat.db";
    private const string LogFileName = "errors.log";

    /// <summary>The demo folder when the marker is beside the running .exe, or null for the normal LocalAppData layout; decided once per run.</summary>
    private static readonly string? PortableFolder = DetectPortableFolder();

    /// <summary>
    /// [CALC] Whether this run is the portable demo, keeping its files beside its own .exe.
    /// False for developer runs, the SeedData tool, and tests, which all use the hidden LocalAppData folder.
    /// </summary>
    public static bool IsPortable => PortableFolder is not null;

    /// <summary>[CALC] Full path to the live database file: in the demo's data\ folder when portable, otherwise directly in %LocalAppData%\BuyTheBoat.</summary>
    public static string DatabasePath => PortableFolder is { } demo
        ? Path.Combine(demo, "data", DatabaseFileName)
        : Path.Combine(LocalAppDataFolder, DatabaseFileName);

    /// <summary>[CALC] Full path to the error log: in the demo's logs\ folder when portable, otherwise beside the database in the LocalAppData folder.</summary>
    public static string LogFilePath => PortableFolder is { } demo
        ? Path.Combine(demo, "logs", LogFileName)
        : Path.Combine(LocalAppDataFolder, LogFileName);

    /// <summary>[CALC] Folder the Export dialogs open to by default in the demo (its exports\ folder), or null off-demo so the dialog keeps its own default location.</summary>
    public static string? DefaultExportFolder => PortableFolder is { } demo
        ? Path.Combine(demo, "exports")
        : null;

    /// <summary>
    /// [CALC] Where Import saves the current database before overwriting it.
    /// In the demo, a timestamped copy in its backups\ folder, so older backups are kept; otherwise the single "&lt;db&gt;.bak" beside the database.
    /// </summary>
    public static string NextImportBackupPath() => PortableFolder is { } demo
        ? Path.Combine(demo, "backups", $"{Path.GetFileNameWithoutExtension(DatabaseFileName)}-{DateTime.Now:yyyy-MM-dd-HHmmss}.db")
        : DatabasePath + ".bak";

    /// <summary>
    /// [WRITES FILE] Creates the demo's data\, logs\, backups\, and exports\ subfolders if they're missing, so they're visible from the demo's first run.
    /// Does nothing outside the demo; call once at startup.
    /// </summary>
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

    /// <summary>The normal layout's folder, %LocalAppData%\BuyTheBoat.</summary>
    private static string LocalAppDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductFolderName);

    /// <summary>
    /// [READS FILE] Returns the running .exe's folder when the portable marker file is beside it, or null otherwise.
    /// This is the only disk check behind the layout decision, and it runs once.
    /// </summary>
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
