using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModpackMerger.Core.Models;
using ModpackMerger.Core.Parsing;

namespace ModpackMerger.Core.Metadata;

/// <summary>
/// Extrait l'identifiant, le nom et la version d'un mod depuis son .jar.
/// Formats gérés : fabric.mod.json, quilt.mod.json, META-INF/neoforge.mods.toml, META-INF/mods.toml, mcmod.info.
/// Ne lève jamais d'exception : un jar illisible donne <see cref="JarMetadata.Empty"/>.
/// </summary>
public static partial class JarMetadataReader
{
    public static JarMetadata Read(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            return Read(archive);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return JarMetadata.Empty;
        }
    }

    public static JarMetadata Read(Stream jarStream)
    {
        try
        {
            using var archive = new ZipArchive(jarStream, ZipArchiveMode.Read, leaveOpen: true);
            return Read(archive);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return JarMetadata.Empty;
        }
    }

    public static JarMetadata Read(ZipArchive jar)
    {
        Func<ZipArchive, JarMetadata?>[] readers = [ReadFabric, ReadQuilt, ReadNeoForge, ReadForge, ReadMcModInfo];
        foreach (var reader in readers)
        {
            try
            {
                if (reader(jar) is { } metadata)
                    return metadata;
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or InvalidOperationException)
            {
                // Fichier de métadonnées corrompu : on tente le format suivant.
            }
        }
        return JarMetadata.Empty;
    }

    private static JarMetadata? ReadFabric(ZipArchive jar)
    {
        if (ZipHelpers.Find(jar, "fabric.mod.json") is not { } entry) return null;
        using var doc = ZipHelpers.ReadJson(entry);
        var root = doc.RootElement;
        return new JarMetadata(root.GetString("id"), root.GetString("name"), root.GetString("version"), ModLoaderKind.Fabric)
        {
            AliasIds = ReadProvides(root),
        };
    }

    private static JarMetadata? ReadQuilt(ZipArchive jar)
    {
        if (ZipHelpers.Find(jar, "quilt.mod.json") is not { } entry) return null;
        using var doc = ZipHelpers.ReadJson(entry);
        if (!doc.RootElement.TryGetProperty("quilt_loader", out var loader)) return null;
        loader.TryGetProperty("metadata", out var meta);
        return new JarMetadata(loader.GetString("id"), meta.GetString("name"), loader.GetString("version"), ModLoaderKind.Quilt)
        {
            AliasIds = ReadProvides(loader),
        };
    }

    private static JarMetadata? ReadNeoForge(ZipArchive jar) =>
        ReadModsToml(jar, "META-INF/neoforge.mods.toml", ModLoaderKind.NeoForge);

    private static JarMetadata? ReadForge(ZipArchive jar) =>
        ReadModsToml(jar, "META-INF/mods.toml", ModLoaderKind.Forge);

    private static JarMetadata? ReadModsToml(ZipArchive jar, string path, ModLoaderKind loader)
    {
        if (ZipHelpers.Find(jar, path) is not { } entry) return null;
        var mods = ModsTomlReader.ReadMods(ZipHelpers.ReadText(entry));
        if (mods.Count == 0) return null;
        var values = mods[0];
        values.TryGetValue("modId", out var id);
        values.TryGetValue("displayName", out var name);
        values.TryGetValue("version", out var version);

        // "${file.jarVersion}" est remplacé au runtime par Implementation-Version du MANIFEST.MF.
        if (version is null || version.Contains("${", StringComparison.Ordinal))
            version = ReadManifestVersion(jar) ?? version;

        return new JarMetadata(id, name, version, loader)
        {
            // Un même jar peut déclarer plusieurs mods (plusieurs tables [[mods]]).
            AliasIds = mods.Skip(1).Select(m => m.GetValueOrDefault("modId")).OfType<string>().ToList(),
        };
    }

    /// <summary>"provides": ["a", "b"] (Fabric) ou [{ "id": "a" }, "b"] (Quilt).</summary>
    private static List<string> ReadProvides(JsonElement element) =>
        element.TryGetProperty("provides", out var provides) && provides.ValueKind == JsonValueKind.Array
            ? provides.EnumerateArray()
                .Select(p => p.ValueKind == JsonValueKind.String ? p.GetString() : p.GetString("id"))
                .OfType<string>()
                .ToList()
            : [];

    private static JarMetadata? ReadMcModInfo(ZipArchive jar)
    {
        if (ZipHelpers.Find(jar, "mcmod.info") is not { } entry) return null;
        using var doc = ZipHelpers.ReadJson(entry);
        var root = doc.RootElement;
        var list = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("modList", out var modList) ? modList
            : default;
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0) return null;
        var first = list[0];
        return new JarMetadata(first.GetString("modid"), first.GetString("name"), first.GetString("version"), ModLoaderKind.LegacyForge);
    }

    [GeneratedRegex(@"^Implementation-Version:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex ImplementationVersion();

    private static string? ReadManifestVersion(ZipArchive jar)
    {
        if (ZipHelpers.Find(jar, "META-INF/MANIFEST.MF") is not { } entry) return null;
        var match = ImplementationVersion().Match(ZipHelpers.ReadText(entry));
        return match.Success ? match.Groups[1].Value : null;
    }
}
