using System.IO.Compression;
using ModpackMerger.Core.Metadata;
using ModpackMerger.Core.Models;

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

    /// <summary>Jars embarqués : le nom réel est lu dans leurs métadonnées, directement depuis l'archive.</summary>
    private static void ResolveEmbedded(ModpackInfo pack, IProgress<string>? log)
    {
        var embedded = pack.Mods.Where(m => m.Source == ModSource.Embedded).ToList();
        if (embedded.Count == 0) return;

        using var archive = ZipFile.OpenRead(pack.SourcePath);
        foreach (var mod in embedded)
        {
            if (archive.GetEntry(mod.ArchiveEntryPath!) is not { } entry) continue;
            using var buffer = new MemoryStream();
            using (var stream = entry.Open()) stream.CopyTo(buffer);
            buffer.Position = 0;

            var metadata = JarMetadataReader.Read(buffer);
            if (!string.IsNullOrWhiteSpace(metadata.Name)) mod.DisplayName = metadata.Name;
            mod.VersionLabel = metadata.Version;
        }
        log?.Report($"{embedded.Count} jar(s) embarqué(s) dans le pack analysé(s).");
    }
}
