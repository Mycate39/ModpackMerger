using System.Net.Http.Json;
using System.Text.Json;
using ModpackMerger.Core.Models;
using ModpackMerger.Core.Parsing;

namespace ModpackMerger.Core.Resolution;

/// <summary>
/// Complète les entrées CurseForge (nom, fichier, URL, hash).
/// - Avec clé API (console.curseforge.com) : appels groupés à l'API officielle, noms de projets exacts.
/// - Sans clé : l'endpoint public de téléchargement redirige vers le CDN ; on lit le nom du .jar
///   dans l'en-tête Location (sans télécharger le fichier).
/// </summary>
public sealed class CurseForgeResolver(HttpClient http, string? apiKey)
{
    private const string ApiBase = "https://api.curseforge.com/v1";

    public static string PublicDownloadUrl(int projectId, int fileId) =>
        $"https://www.curseforge.com/api/v1/mods/{projectId}/files/{fileId}/download";

    public static string CdnUrl(int fileId, string fileName) =>
        $"https://edge.forgecdn.net/files/{fileId / 1000}/{fileId % 1000}/{Uri.EscapeDataString(fileName)}";

    public async Task ResolveAsync(IReadOnlyList<ModEntry> mods, IProgress<string>? log, CancellationToken ct)
    {
        var targets = mods.Where(m => m.Source == ModSource.CurseForge).ToList();
        if (targets.Count == 0) return;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                await ResolveWithApiAsync(targets, ct);
                log?.Report($"CurseForge : {targets.Count} mods résolus via l'API officielle.");
                return;
            }
            catch (HttpRequestException ex)
            {
                log?.Report($"API CurseForge indisponible ({ex.StatusCode?.ToString() ?? ex.Message}), repli sur la méthode sans clé.");
            }
        }

        var failures = await ResolveWithoutKeyAsync(targets, ct);
        log?.Report($"CurseForge : {targets.Count - failures}/{targets.Count} mods résolus (sans clé API, noms déduits des fichiers).");
    }

    private async Task ResolveWithApiAsync(List<ModEntry> targets, CancellationToken ct)
    {
        var files = await PostAsync("/mods/files", new { fileIds = targets.Select(m => m.FileId!.Value).Distinct() }, ct);
        var filesById = files.EnumerateArray().ToDictionary(f => f.GetProperty("id").GetInt32());

        var projects = await PostAsync("/mods", new { modIds = targets.Select(m => m.ProjectId!.Value).Distinct() }, ct);
        var namesById = projects.EnumerateArray().ToDictionary(p => p.GetProperty("id").GetInt32(), p => p.GetString("name"));

        foreach (var mod in targets)
        {
            if (namesById.GetValueOrDefault(mod.ProjectId!.Value) is { } name) mod.DisplayName = name;
            if (!filesById.TryGetValue(mod.FileId!.Value, out var file)) continue;

            mod.FileName = file.GetString("fileName");
            mod.VersionLabel = file.GetString("displayName");
            if (file.TryGetProperty("fileLength", out var len) && len.TryGetInt64(out var l)) mod.FileSize = l;
            if (file.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
            {
                // algo 1 = SHA-1, 2 = MD5
                mod.Sha1 = hashes.EnumerateArray()
                    .FirstOrDefault(h => h.TryGetProperty("algo", out var a) && a.GetInt32() == 1)
                    .GetString("value");
            }

            mod.DownloadUrls.Clear();
            // downloadUrl est null quand l'auteur a désactivé la distribution tierce : le CDN reste accessible.
            if (file.GetString("downloadUrl") is { } url) mod.DownloadUrls.Add(url);
            else if (mod.FileName is not null) mod.DownloadUrls.Add(CdnUrl(mod.FileId.Value, mod.FileName));
            mod.DownloadUrls.Add(PublicDownloadUrl(mod.ProjectId.Value, mod.FileId.Value));
        }
    }

    private async Task<JsonElement> PostAsync(string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("x-api-key", apiKey);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static async Task<int> ResolveWithoutKeyAsync(List<ModEntry> targets, CancellationToken ct)
    {
        using var noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        noRedirect.DefaultRequestHeaders.UserAgent.ParseAdd(HttpClientFactory.UserAgent);
        var failures = 0;

        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (mod, token) =>
        {
            var publicUrl = PublicDownloadUrl(mod.ProjectId!.Value, mod.FileId!.Value);
            mod.DownloadUrls.Clear();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, publicUrl);
                using var response = await noRedirect.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    var fileName = Uri.UnescapeDataString(location.AbsolutePath.Split('/')[^1]);
                    mod.FileName = fileName;
                    mod.DisplayName = ModNameFormatter.FromFileName(fileName);
                    mod.DownloadUrls.Add(location.ToString());
                }
                else
                {
                    Interlocked.Increment(ref failures);
                }
            }
            catch (HttpRequestException)
            {
                Interlocked.Increment(ref failures);
            }
            mod.DownloadUrls.Add(publicUrl);
        });

        return failures;
    }
}
