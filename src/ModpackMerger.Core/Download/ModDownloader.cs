using System.IO.Compression;
using System.Security.Cryptography;
using ModpackMerger.Core.Metadata;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Download;

public sealed record DownloadedMod(ModEntry Entry, string LocalPath, string Sha1, JarMetadata Metadata)
{
    public string FileName => Path.GetFileName(LocalPath);
}

public sealed record DownloadFailure(ModEntry Entry, string Reason);

public sealed record DownloadProgress(int Completed, int Total, string CurrentItem);

public sealed record DownloadResult(IReadOnlyList<DownloadedMod> Downloaded, IReadOnlyList<DownloadFailure> Failures);

/// <summary>
/// Récupère les jars sélectionnés dans un cache local (%LocalAppData%\ModpackMerger\cache),
/// vérifie leur intégrité (SHA-1 / SHA-512) et lit leurs métadonnées.
/// </summary>
public sealed class ModDownloader(HttpClient http, string cacheDirectory, int maxParallelDownloads = 4)
{
    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModpackMerger", "cache");

    public async Task<DownloadResult> DownloadAsync(
        ModpackInfo pack,
        IReadOnlyList<ModEntry> mods,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var downloaded = new List<DownloadedMod>();
        var failures = new List<DownloadFailure>();
        var completed = 0;
        var gate = new object();

        void Done(ModEntry mod, DownloadedMod? ok, string? error)
        {
            lock (gate)
            {
                if (ok is not null) downloaded.Add(ok);
                else failures.Add(new DownloadFailure(mod, error ?? "Erreur inconnue"));
                completed++;
                progress?.Report(new DownloadProgress(completed, mods.Count, mod.DisplayName));
            }
        }

        // Les jars embarqués sont extraits séquentiellement (ZipArchive n'est pas thread-safe).
        var embedded = mods.Where(m => m.Source == ModSource.Embedded).ToList();
        if (embedded.Count > 0)
        {
            using var archive = ZipFile.OpenRead(pack.SourcePath);
            foreach (var mod in embedded)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var entry = archive.GetEntry(mod.ArchiveEntryPath!)
                        ?? throw new FileNotFoundException("Entrée absente de l'archive.");
                    var target = CachePath(mod, entry.Name);
                    entry.ExtractToFile(target, overwrite: true);
                    Done(mod, Inspect(mod, target), null);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    Done(mod, null, ex.Message);
                }
            }
        }

        var remote = mods.Where(m => m.Source != ModSource.Embedded).ToList();
        await Parallel.ForEachAsync(remote, new ParallelOptions { MaxDegreeOfParallelism = maxParallelDownloads, CancellationToken = ct },
            async (mod, token) =>
            {
                try
                {
                    Done(mod, await DownloadRemoteAsync(mod, token), null);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                           || ex is TaskCanceledException && !token.IsCancellationRequested)
                {
                    Done(mod, null, ex.Message);
                }
            });

        return new DownloadResult(downloaded, failures);
    }

    private async Task<DownloadedMod> DownloadRemoteAsync(ModEntry mod, CancellationToken ct)
    {
        if (mod.DownloadUrls.Count == 0)
            throw new InvalidDataException("Aucune URL de téléchargement (résolution échouée ?).");

        var fileName = mod.FileName ?? $"{mod.Key}.jar";
        var target = CachePath(mod, fileName);

        if (File.Exists(target) && IsValid(mod, target))
            return Inspect(mod, target);

        Exception? last = null;
        foreach (var url in mod.DownloadUrls)
        {
            try
            {
                var temp = target + ".part";
                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    await using var output = File.Create(temp);
                    await response.Content.CopyToAsync(output, ct);
                }

                if (!IsValid(mod, temp))
                {
                    File.Delete(temp);
                    throw new InvalidDataException("Hash du fichier téléchargé invalide.");
                }

                File.Move(temp, target, overwrite: true);
                return Inspect(mod, target);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                last = ex;
            }
        }
        throw last!;
    }

    private string CachePath(ModEntry mod, string fileName)
    {
        var safeKey = string.Concat(mod.Key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (safeKey.Length > 80) safeKey = safeKey[..80];
        var dir = Path.Combine(cacheDirectory, safeKey);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Path.GetFileName(fileName));
    }

    private static bool IsValid(ModEntry mod, string path)
    {
        if (new FileInfo(path).Length == 0) return false;
        if (mod.Sha1 is not null) return HashFile(path, SHA1.Create()).Equals(mod.Sha1, StringComparison.OrdinalIgnoreCase);
        if (mod.Sha512 is not null) return HashFile(path, SHA512.Create()).Equals(mod.Sha512, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static DownloadedMod Inspect(ModEntry mod, string path) =>
        new(mod, path, Sha1Of(path), JarMetadataReader.Read(path));

    public static string Sha1Of(string path) => HashFile(path, SHA1.Create());

    private static string HashFile(string path, HashAlgorithm algorithm)
    {
        using (algorithm)
        using (var stream = File.OpenRead(path))
            return Convert.ToHexStringLower(algorithm.ComputeHash(stream));
    }
}
