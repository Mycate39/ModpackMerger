using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ModpackMerger.Core.Updates;

namespace ModpackMerger.Mac.Services;

/// <summary>
/// Mise à jour automatique depuis les releases GitHub : on récupère le zip macOS de la bonne architecture
/// (empreinte SHA-256 contrôlée), puis un petit script remplace le paquet .app une fois l'application fermée et la relance.
/// Le zip étant téléchargé par l'application et non par le navigateur, il n'est pas mis en quarantaine par Gatekeeper.
/// </summary>
public sealed class UpdateService(HttpClient http)
{
    public const string UpdatedArgument = "--updated";

    /// <summary>Source des releases ; surchargeable (MODPACKMERGER_UPDATE_FEED) pour tester sans publier.</summary>
    private static string FeedUrl =>
        Environment.GetEnvironmentVariable("MODPACKMERGER_UPDATE_FEED") is { Length: > 0 } custom ? custom : ReleaseFeed.LatestReleaseUrl;

    private static string DownloadDirectory => Path.Combine(Path.GetTempPath(), "ModpackMerger-update");

    /// <summary>Suffixe du zip publié pour cette machine : ModpackMerger-X.Y.Z-macos-arm64.zip ou -macos-x64.zip.</summary>
    public static string AssetSuffix =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "-macos-arm64.zip" : "-macos-x64.zip";

    /// <summary>Seule l'application empaquetée (.app) peut se remplacer ; dotnet run ne se met pas à jour, sauf source de test.</summary>
    public static bool IsSupported =>
        AppPaths.AppBundlePath is not null || Environment.GetEnvironmentVariable("MODPACKMERGER_UPDATE_FEED") is { Length: > 0 };

    /// <summary>Vrai si la mise à jour peut être installée sur place (sinon, on ouvre la page de la release).</summary>
    public static bool CanSelfUpdate => OperatingSystem.IsMacOS() && AppPaths.AppBundlePath is not null;

    /// <summary>Renvoie la mise à jour disponible, ou null (à jour, hors ligne, API indisponible…).</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return ReleaseFeed.Parse(await response.Content.ReadAsStringAsync(ct), AppPaths.CurrentVersion,
                name => name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Télécharge le zip dans le dossier temporaire et vérifie son empreinte. Renvoie son chemin.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct)
    {
        if (Directory.Exists(DownloadDirectory))
            Directory.Delete(DownloadDirectory, recursive: true);
        Directory.CreateDirectory(DownloadDirectory);
        var target = Path.Combine(DownloadDirectory, $"ModpackMerger-{update.Version}.zip");

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
    /// Décompresse le zip (ditto conserve permissions et signature), puis lance un script qui attend la fermeture
    /// de l'application, remplace le paquet .app et le relance. L'appelant doit se fermer juste après.
    /// </summary>
    public static async Task PrepareAndLaunchUpdaterAsync(string zipPath)
    {
        var bundle = AppPaths.AppBundlePath ?? throw new InvalidOperationException("L'application ne tourne pas depuis un paquet .app.");
        var extractDir = Path.Combine(DownloadDirectory, "extracted");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);

        await RunAsync("/usr/bin/ditto", "-x", "-k", zipPath, extractDir);
        var newBundle = Directory.EnumerateDirectories(extractDir, "*.app").FirstOrDefault()
                        ?? throw new InvalidDataException("Le zip de mise à jour ne contient pas d'application (.app).");

        // Arguments passés à sh -c : $1 = PID à attendre, $2 = ancien .app, $3 = nouveau .app.
        const string script = """
            while kill -0 "$1" 2>/dev/null; do sleep 0.3; done
            rm -rf "$2" && mv "$3" "$2" || exit 1
            xattr -dr com.apple.quarantine "$2" 2>/dev/null
            open "$2" --args --updated
            """;
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var arg in new[] { "-c", script, "sh", Environment.ProcessId.ToString(), bundle, newBundle })
            info.ArgumentList.Add(arg);
        Process.Start(info);
    }

    private static async Task RunAsync(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Impossible de lancer {file}.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new IOException($"{Path.GetFileName(file)} a échoué : {error.Trim()}");
    }
}
