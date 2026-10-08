using System.IO.Compression;
using System.Text.Json;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Parsing;

/// <summary>
/// Lit un .mrpack : modrinth.index.json (URLs + hashes) + jars éventuels dans overrides/ et client-overrides/.
/// Seuls les fichiers sous "mods/" sont retenus (pas les resourcepacks/shaderpacks).
/// </summary>
public sealed class ModrinthParser : IModpackParser
{
    public bool CanParse(ZipArchive archive) => ZipHelpers.Find(archive, "modrinth.index.json") is not null;

    public ModpackInfo Parse(ZipArchive archive, string sourcePath)
    {
        using var doc = ZipHelpers.ReadJson(ZipHelpers.Find(archive, "modrinth.index.json")!);
        var root = doc.RootElement;

        if (root.GetString("game") is { } game && game != "minecraft")
            throw new ModpackFormatException($"Ce .mrpack cible le jeu « {game} », pas Minecraft.");

        var mods = new List<ModEntry>();
        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in files.EnumerateArray())
            {
                var path = file.GetString("path")?.Replace('\\', '/');
                if (path is null
                    || !path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)
                    || !path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileName(path);
                file.TryGetProperty("hashes", out var hashes);
                file.TryGetProperty("env", out var env);
                var clientEnv = env.GetString("client");

                var urls = file.TryGetProperty("downloads", out var downloads) && downloads.ValueKind == JsonValueKind.Array
                    ? downloads.EnumerateArray().Select(d => d.GetString()).OfType<string>().ToList()
                    : [];

                mods.Add(new ModEntry
                {
                    Source = ModSource.Modrinth,
                    DisplayName = ModNameFormatter.FromFileName(fileName),
                    FileName = fileName,
                    DownloadUrls = urls,
                    Sha1 = hashes.GetString("sha1"),
                    Sha512 = hashes.GetString("sha512"),
                    FileSize = file.TryGetProperty("fileSize", out var size) && size.TryGetInt64(out var s) ? s : null,
                    Required = clientEnv != "optional",
                    ClientSupported = clientEnv != "unsupported",
                });
            }
        }

        // client-overrides a priorité sur overrides : on garde la dernière occurrence par nom de fichier.
        var embedded = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { "overrides", "client-overrides" })
            foreach (var entry in ZipHelpers.EmbeddedJars(archive, dir))
                embedded[entry.Name] = CurseForgeParser.EmbeddedEntry(entry);
        mods.AddRange(embedded.Values);

        string? mcVersion = null, loader = null;
        if (root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Object)
        {
            foreach (var dep in deps.EnumerateObject())
            {
                if (dep.Name == "minecraft") mcVersion = dep.Value.GetString();
                else loader ??= $"{dep.Name} {dep.Value.GetString()}";
            }
        }

        return new ModpackInfo
        {
            SourcePath = sourcePath,
            Format = ModpackFormat.Modrinth,
            Name = root.GetString("name") ?? Path.GetFileNameWithoutExtension(sourcePath),
            Version = root.GetString("versionId"),
            MinecraftVersion = mcVersion,
            Loader = loader,
            Mods = mods,
        };
    }
}
