using System.IO.Compression;
using System.Text.Json;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Parsing;

/// <summary>
/// Lit un export CurseForge : manifest.json (projectID/fileID) + jars éventuels dans overrides/mods.
/// Le manifest ne contient ni noms ni fichiers : ils sont complétés par <see cref="Resolution.CurseForgeResolver"/>.
/// </summary>
public sealed class CurseForgeParser : IModpackParser
{
    public bool CanParse(ZipArchive archive) => ZipHelpers.Find(archive, "manifest.json") is not null;

    public ModpackInfo Parse(ZipArchive archive, string sourcePath)
    {
        using var doc = ZipHelpers.ReadJson(ZipHelpers.Find(archive, "manifest.json")!);
        var root = doc.RootElement;

        if (root.GetString("manifestType") is { } type && type != "minecraftModpack")
            throw new ModpackFormatException($"manifest.json de type inattendu : « {type} ».");

        var mods = new List<ModEntry>();
        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in files.EnumerateArray())
            {
                if (!file.TryGetProperty("projectID", out var p) || !file.TryGetProperty("fileID", out var f))
                    continue;

                var projectId = p.GetInt32();
                mods.Add(new ModEntry
                {
                    Source = ModSource.CurseForge,
                    DisplayName = $"Projet CurseForge #{projectId}",
                    ProjectId = projectId,
                    FileId = f.GetInt32(),
                    Required = !file.TryGetProperty("required", out var r) || r.ValueKind != JsonValueKind.False,
                });
            }
        }

        var overrides = root.GetString("overrides") ?? "overrides";
        mods.AddRange(ZipHelpers.EmbeddedJars(archive, overrides).Select(EmbeddedEntry));

        string? mcVersion = null, loader = null;
        if (root.TryGetProperty("minecraft", out var minecraft))
        {
            mcVersion = minecraft.GetString("version");
            if (minecraft.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array)
            {
                var all = loaders.EnumerateArray().ToList();
                var primary = all.FirstOrDefault(l => l.TryGetProperty("primary", out var pr) && pr.ValueKind == JsonValueKind.True);
                loader = (primary.ValueKind == JsonValueKind.Object ? primary : all.FirstOrDefault()).GetString("id");
            }
        }

        return new ModpackInfo
        {
            SourcePath = sourcePath,
            Format = ModpackFormat.CurseForge,
            Name = root.GetString("name") ?? Path.GetFileNameWithoutExtension(sourcePath),
            Version = root.GetString("version"),
            Author = root.GetString("author"),
            MinecraftVersion = mcVersion,
            Loader = loader,
            Mods = mods,
        };
    }

    internal static ModEntry EmbeddedEntry(ZipArchiveEntry entry) => new()
    {
        Source = ModSource.Embedded,
        DisplayName = ModNameFormatter.FromFileName(entry.Name),
        FileName = entry.Name,
        FileSize = entry.Length,
        ArchiveEntryPath = entry.FullName,
    };
}
