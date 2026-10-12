using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using ModpackMerger.Core.Updates;

namespace ModpackMerger.App.Services;

/// <summary>
/// Mise à jour automatique depuis les releases GitHub : vérification, téléchargement de l'installeur
/// (empreinte SHA-256 contrôlée), puis remplacement de la copie installée par un processus séparé.
/// </summary>
public sealed class UpdateService(HttpClient http)
{
    public const string UpdateArgument = "--update";
    public const string UpdatedArgument = "--updated";
    public const string WaitPidArgument = "--wait-pid";
    public const string DesktopShortcutArgument = "--desktop-shortcut";

    /// <summary>Source des releases ; surchargeable (MODPACKMERGER_UPDATE_FEED) pour tester sans publier.</summary>
    private static string FeedUrl =>
        Environment.GetEnvironmentVariable("MODPACKMERGER_UPDATE_FEED") is { Length: > 0 } custom ? custom : ReleaseFeed.LatestReleaseUrl;

    private static string DownloadDirectory => Path.Combine(Path.GetTempPath(), "ModpackMerger-update");

    /// <summary>Les builds de développement (dotnet run) ne se mettent pas à jour, sauf source de test explicite.</summary>
    public static bool IsSupported =>
        AppInstaller.IsSingleFileBuild || Environment.GetEnvironmentVariable("MODPACKMERGER_UPDATE_FEED") is { Length: > 0 };

    /// <summary>Renvoie la mise à jour disponible, ou null (à jour, hors ligne, API indisponible…).</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return ReleaseFeed.Parse(await response.Content.ReadAsStringAsync(ct), AppInstaller.CurrentVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Télécharge l'installeur dans %TEMP% et vérifie son empreinte. Renvoie son chemin.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct)
    {
        if (Directory.Exists(DownloadDirectory))
        {
            foreach (var old in Directory.EnumerateFiles(DownloadDirectory))
                try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        Directory.CreateDirectory(DownloadDirectory);
        var target = Path.Combine(DownloadDirectory, $"ModpackMerger-Setup-{update.Version}.exe");

        using (var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(target);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                if (total > 0) progress?.Report((double)received / total);
            }
        }

        if (update.Sha256 is { } expected)
        {
            await using var stream = File.OpenRead(target);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new InvalidDataException("Le fichier téléchargé est corrompu (empreinte SHA-256 différente).");
            }
        }
        return target;
    }

    /// <summary>
    /// Lance l'installeur téléchargé en mode mise à jour : il attend la fermeture de l'application,
    /// remplace la copie installée puis la relance. L'appelant doit se fermer juste après.
    /// </summary>
    public static void LaunchUpdater(string installerPath)
    {
        var info = new ProcessStartInfo(installerPath) { UseShellExecute = false };
        info.ArgumentList.Add(UpdateArgument);
        info.ArgumentList.Add(WaitPidArgument);
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        if (AppInstaller.HasDesktopShortcut) info.ArgumentList.Add(DesktopShortcutArgument);
        Process.Start(info);
    }

    /// <summary>Côté installeur : attend la fermeture de l'ancienne version, installe, relance. Renvoie un message d'erreur ou null.</summary>
    public static string? ApplyUpdate(IReadOnlyList<string> args)
    {
        var pidIndex = args.ToList().FindIndex(a => a.Equals(WaitPidArgument, StringComparison.OrdinalIgnoreCase));
        if (pidIndex >= 0 && pidIndex + 1 < args.Count && int.TryParse(args[pidIndex + 1], out var pid))
        {
            try
            {
                using var previous = Process.GetProcessById(pid);
                previous.WaitForExit(TimeSpan.FromSeconds(30));
            }
            catch (ArgumentException)
            {
                // Déjà fermée.
            }
        }

        var desktop = args.Contains(DesktopShortcutArgument, StringComparer.OrdinalIgnoreCase);
        // L'exécutable peut rester verrouillé quelques instants après la fermeture du processus.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                AppInstaller.Install(desktop);
                break;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(500);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return ex.Message;
            }
        }

        AppInstaller.LaunchInstalled([UpdatedArgument]);
        return null;
    }
}
