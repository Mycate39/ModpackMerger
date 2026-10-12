using ModpackMerger.Core.Metadata;
using ModpackMerger.Core.Models;
using ModpackMerger.Core.Parsing;

namespace ModpackMerger.Core.Resolution;

/// <summary>Orchestre la résolution des noms pour toutes les sources d'un modpack.</summary>
public sealed class ModpackResolver(HttpClient http, string? curseForgeApiKey)
{
    public async Task ResolveAsync(ModpackInfo pack, IProgress<string>? log, CancellationToken ct)
    {
        ResolveEmbedded(pack, log);
        await Task.WhenAll(
            new CurseForgeResolver(http, curseForgeApiKey).ResolveAsync(pack.Mods, log, ct),
            new ModrinthResolver(http).ResolveAsync(pack.Mods, log, ct));
    }

    /// <summary>Jars présents dans le pack : le nom réel est lu dans leurs métadonnées, sans extraction.</summary>
    private static void ResolveEmbedded(ModpackInfo pack, IProgress<string>? log)
    {
        var embedded = pack.Mods.Where(m => m.Source == ModSource.Embedded).ToList();
        if (embedded.Count == 0) return;

        using var fs = PackFileSystem.Open(pack.SourcePath);
        foreach (var mod in embedded)
        {
            if (fs.Find(mod.ArchiveEntryPath!) is null) continue;
            using var stream = fs.Open(mod.ArchiveEntryPath!);
            var metadata = JarMetadataReader.Read(stream);
            if (!string.IsNullOrWhiteSpace(metadata.Name)) mod.DisplayName = metadata.Name;
            mod.VersionLabel = metadata.Version;
        }
        log?.Report($"{embedded.Count} jar(s) du pack analysé(s).");
    }
}
