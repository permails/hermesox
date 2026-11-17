namespace HermesDesktop.Core.Storage;

/// <summary>
/// Resolves local data storage directory:
/// - Portable Mode: if a "data" directory sits beside the executable, store all local files there.
/// - Standard Mode: %LOCALAPPDATA%\HermesDesktop on Windows (or LocalApplicationData on macOS/Linux).
/// </summary>
public static class AppDataPaths
{
    public static string GetDataDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var portableDir = Path.Combine(baseDir, "data");
        if (Directory.Exists(portableDir))
        {
            return portableDir;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        var appDir = Path.Combine(localAppData, "HermesDesktop");
        Directory.CreateDirectory(appDir);

        // Migrate saved config/credentials from earlier HermesDesktop data directory if present
        var legacyDir = Path.Combine(localAppData, "HermesConduit");
        if (Directory.Exists(legacyDir) && !File.Exists(Path.Combine(appDir, "cache.db")))
        {
            foreach (var fileName in new[] { "cache.db", "vault.dat", "settings.json" })
            {
                var src = Path.Combine(legacyDir, fileName);
                var dst = Path.Combine(appDir, fileName);
                if (File.Exists(src) && !File.Exists(dst))
                {
                    try { File.Copy(src, dst, overwrite: false); } catch { /* Non-fatal */ }
                }
            }
        }
        return appDir;
    }

    public static bool IsPortableMode => Directory.Exists(Path.Combine(AppContext.BaseDirectory, "data"));

    public static string CacheDatabasePath => Path.Combine(GetDataDirectory(), "cache.db");
    public static string VaultFilePath => Path.Combine(GetDataDirectory(), "vault.dat");
    public static string SettingsFilePath => Path.Combine(GetDataDirectory(), "settings.json");
}
