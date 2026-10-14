using System.Reflection;

namespace ModpackMerger.Mac.Services;

/// <summary>Emplacements standard de macOS (~/Library/…), avec repli sur les dossiers .NET ailleurs (développement sous Windows).</summary>
public static class AppPaths
{
    private const string AppFolder = "ModpackMerger";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>~/Library/Application Support/ModpackMerger</summary>
    public static string SupportDirectory => OperatingSystem.IsMacOS()
        ? Path.Combine(Home, "Library", "Application Support", AppFolder)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);

    /// <summary>~/Library/Caches/ModpackMerger/mods</summary>
    public static string CacheDirectory => OperatingSystem.IsMacOS()
        ? Path.Combine(Home, "Library", "Caches", AppFolder, "mods")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder, "cache");

    /// <summary>Dossier mods du lanceur Minecraft officiel sur Mac.</summary>
    public static string DefaultMinecraftMods => Path.Combine(Home, "Library", "Application Support", "minecraft", "mods");

    /// <summary>Version de l'application, sans le hash de commit (ex. "1.1.0").</summary>
    public static string CurrentVersion { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    /// <summary>Chemin du paquet .app en cours d'exécution (…/Modpack Merger.app), ou null hors paquet (dotnet run).</summary>
    public static string? AppBundlePath
    {
        get
        {
            // Exécutable dans Modpack Merger.app/Contents/MacOS/
            var macOs = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('/', '\\'));
            var bundle = macOs.Parent?.Parent;
            return macOs.Name == "MacOS" && macOs.Parent?.Name == "Contents"
                   && bundle is not null && bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                ? bundle.FullName
                : null;
        }
    }
}
