using System.Net.Http.Json;
using System.Text.Json;
using ModpackMerger.Core.Models;
using ModpackMerger.Core.Parsing;

namespace ModpackMerger.Core.Resolution;

/// <summary>Récupère les vrais noms de projets Modrinth à partir des hashes SHA-1 (API publique, sans clé).</summary>
public sealed class ModrinthResolver(HttpClient http)
{
    private const string ApiBase = "https://api.modrinth.com/v2";

    public async Task ResolveAsync(IReadOnlyList<ModEntry> mods, IProgress<string>? log, CancellationToken ct)
    {
        var targets = mods.Where(m => m.Source == ModSource.Modrinth && m.Sha1 is not null).ToList();
        if (targets.Count == 0) return;

        try
        {
            // hash -> (project_id, version_number)
            var versions = new Dictionary<string, (string ProjectId, string? Version)>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in targets.Chunk(500))
            {
                using var response = await http.PostAsJsonAsync($"{ApiBase}/version_files",
                    new { hashes = chunk.Select(m => m.Sha1), algorithm = "sha1" }, ct);
                response.EnsureSuccessStatusCode();
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    if (entry.Value.GetString("project_id") is { } projectId)
                        versions[entry.Name] = (projectId, entry.Value.GetString("version_number"));
                }
            }

            var titles = new Dictionary<string, string>();
            foreach (var chunk in versions.Values.Select(v => v.ProjectId).Distinct().Chunk(100))
            {
                var ids = Uri.EscapeDataString(JsonSerializer.Serialize(chunk));
                using var doc = JsonDocument.Parse(await http.GetStringAsync($"{ApiBase}/projects?ids={ids}", ct));
                foreach (var project in doc.RootElement.EnumerateArray())
                {
                    if (project.GetString("id") is { } id && project.GetString("title") is { } title)
                        titles[id] = title;
                }
            }

            foreach (var mod in targets)
            {
                if (!versions.TryGetValue(mod.Sha1!, out var version)) continue;
                if (titles.TryGetValue(version.ProjectId, out var title)) mod.DisplayName = title;
                mod.VersionLabel = version.Version;
            }

            log?.Report($"Modrinth : {versions.Count}/{targets.Count} mods identifiés.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log?.Report($"Modrinth : résolution des noms impossible ({ex.Message}). Les noms de fichiers sont conservés.");
        }
    }
}
