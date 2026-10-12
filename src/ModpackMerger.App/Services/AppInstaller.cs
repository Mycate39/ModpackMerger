using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ModpackMerger.App.Services;

/// <summary>
/// Installation « par utilisateur » de l'exécutable autonome, sans droits administrateur :
/// copie dans %LocalAppData%\Programs\Modpack Merger, raccourcis menu Démarrer / bureau,
/// et entrée dans Paramètres Windows › Applications (désinstallation via <c>--uninstall</c>).
/// </summary>
public static class AppInstaller
{
    public const string AppName = "Modpack Merger";
    public const string UninstallArgument = "--uninstall";
    public const string PortableArgument = "--portable";

    private const string ExeName = "ModpackMerger.exe";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ModpackMerger";

    public static string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static string InstalledExe => Path.Combine(InstallDirectory, ExeName);

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    /// <summary>Version de l'exécutable en cours, sans le hash de commit (ex. "1.0.0").</summary>
    public static string CurrentVersion { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    /// <summary>
    /// Seul l'exécutable publié en fichier unique est installable : un build de développement
    /// (dotnet run) a besoin de ses DLL à côté de lui.
    /// </summary>
    public static bool IsSingleFileBuild => !File.Exists(Path.Combine(AppContext.BaseDirectory, "ModpackMerger.Core.dll"));

    public static bool HasDesktopShortcut => File.Exists(DesktopShortcut);

    public static bool IsRunningInstalledCopy =>
        Environment.ProcessPath is { } path
        && string.Equals(Path.GetFullPath(path), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public static bool ShouldOfferInstall(IReadOnlyCollection<string> args) =>
        IsSingleFileBuild && !IsRunningInstalledCopy && !args.Contains(PortableArgument, StringComparer.OrdinalIgnoreCase);

    /// <summary>Version de la copie installée, ou null si l'application n'est pas installée.</summary>
    public static string? InstalledVersion =>
        File.Exists(InstalledExe)
            ? (FileVersionInfo.GetVersionInfo(InstalledExe).ProductVersion ?? "0.0.0").Split('+')[0]
            : null;

    /// <summary>Copie l'exécutable courant et enregistre l'application. Lève IOException si la copie installée est ouverte.</summary>
    public static void Install(bool desktopShortcut)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de l'exécutable inconnu.");
        Directory.CreateDirectory(InstallDirectory);
        File.Copy(source, InstalledExe, overwrite: true);

        CreateShortcut(StartMenuShortcut);
        if (desktopShortcut) CreateShortcut(DesktopShortcut);
        else if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", CurrentVersion);
        key.SetValue("Publisher", "Mycate39");
        key.SetValue("DisplayIcon", $"\"{InstalledExe}\",0");
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("UninstallString", $"\"{InstalledExe}\" {UninstallArgument}");
        key.SetValue("URLInfoAbout", "https://github.com/Mycate39/ModpackMerger");
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Retire raccourcis, entrée Applications et cache de téléchargement. Le dossier d'installation
    /// (qui contient l'exécutable en cours) est supprimé juste après la fermeture de l'application.
    /// Les réglages (%AppData%\ModpackMerger) sont conservés.
    /// </summary>
    public static void Uninstall()
    {
        foreach (var shortcut in new[] { StartMenuShortcut, DesktopShortcut })
            if (File.Exists(shortcut)) File.Delete(shortcut);

        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackMerger");
        try
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
        catch (IOException)
        {
            // Cache non critique : on ne bloque pas la désinstallation.
        }

        if (Directory.Exists(InstallDirectory))
        {
            // L'exécutable en cours est dans ce dossier : un processus annexe attend sa fermeture puis le supprime.
            // ("timeout" de cmd échoue sans console, d'où PowerShell et Wait-Process.)
            var directory = InstallDirectory.Replace("'", "''");
            var script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
                         $"Remove-Item -LiteralPath '{directory}' -Recurse -Force -ErrorAction SilentlyContinue";
            var cleanup = new ProcessStartInfo("powershell.exe") { CreateNoWindow = true, UseShellExecute = false };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script })
                cleanup.ArgumentList.Add(arg);
            Process.Start(cleanup);
        }
    }

    /// <summary>Lance la copie installée en lui transmettant les arguments (ex. un pack à ouvrir).</summary>
    public static void LaunchInstalled(IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(InstalledExe) { UseShellExecute = false, WorkingDirectory = InstallDirectory };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        Process.Start(info);
    }

    private static void CreateShortcut(string path)
    {
        // Raccourci .lnk via le composant COM WScript.Shell, présent sur toutes les versions de Windows.
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell indisponible.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = InstalledExe;
            shortcut.WorkingDirectory = InstallDirectory;
            shortcut.IconLocation = InstalledExe + ",0";
            shortcut.Description = "Injecte les mods d'un modpack dans ton modpack Minecraft";
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
