using System.Text.Json;
using System.Text.RegularExpressions;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Parsing;

/// <summary>
/// Lit un modpack « dossier classique » : les jars réellement présents.
/// Reconnaît les instances CurseForge App (minecraftinstance.json), Prism/MultiMC (mmc-pack.json,
/// instance.cfg, mods dans .minecraft/mods), un .minecraft classique, ou un simple dossier de jars.
/// </summary>
public sealed partial class FolderParser : IModpackParser
{
    /// <summary>Emplacements du dossier "mods" par ordre de priorité (la racine en dernier recours).</summary>
    internal static readonly string[] ModsDirectories = ["mods", ".minecraft/mods", "minecraft/mods", ""];

    /// <summary>Fichiers d'instance utiles pour les informations du pack.</summary>
    internal static readonly string[] InstanceFiles = ["minecraftinstance.json", "mmc-pack.json", "instance.cfg"];

    public bool CanParse(IPackFileSystem pack) => FindModsDirectory(pack) is not null;

    /// <summary>Vrai si le dossier contient un vrai sous-dossier "mods" garni (prioritaire sur un manifest).</summary>
    public static bool HasModsSubfolder(IPackFileSystem pack) =>
        FindModsDirectory(pack) is { Length: > 0 };

    public ModpackInfo Parse(IPackFileSystem pack, string sourcePath)
    {
        var modsDir = FindModsDirectory(pack)
            ?? throw new ModpackFormatException("Aucun fichier .jar trouvé dans ce dossier ni dans son sous-dossier « mods ».");

        var mods = pack.JarsIn(modsDir).Select(CurseForgeParser.EmbeddedEntry).ToList();

        string? name = null, mcVersion = null, loader = null;
        if (pack.Find("minecraftinstance.json") is { } cf) ReadCurseForgeInstance(pack, cf, ref name, ref mcVersion, ref loader);
        if (pack.Find("mmc-pack.json") is { } mmc) ReadMmcPack(pack, mmc, ref mcVersion, ref loader);
        if (name is null && pack.Find("instance.cfg") is { } cfg) name = ReadInstanceCfgName(pack, cfg);

        return new ModpackInfo
        {
            SourcePath = sourcePath,
            Format = ModpackFormat.Folder,
            Name = name ?? FolderDisplayName(sourcePath),
            MinecraftVersion = mcVersion,
            Loader = loader,
            Mods = mods,
        };
    }

    private static string? FindModsDirectory(IPackFileSystem pack) =>
        ModsDirectories.FirstOrDefault(dir => pack.JarsIn(dir).Any());

    /// <summary>"…/MonPack/mods" → "MonPack" ; "…/MonPack/.minecraft" → "MonPack".</summary>
    private static string FolderDisplayName(string path)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(path).TrimEnd('\\', '/'));
        while (dir.Parent is not null && dir.Name.ToLowerInvariant() is "mods" or ".minecraft" or "minecraft")
            dir = dir.Parent;
        return dir.Name;
    }

    private static void ReadCurseForgeInstance(IPackFileSystem pack, PackFile file, ref string? name, ref string? mcVersion, ref string? loader)
    {
        try
        {
            using var doc = pack.ReadJson(file);
            var root = doc.RootElement;
            name = root.GetString("name");
            mcVersion = root.GetString("gameVersion");
            if (root.TryGetProperty("baseModLoader", out var baseLoader)) loader = baseLoader.GetString("name");
        }
        catch (JsonException)
        {
            // Fichier d'instance illisible : les infos restent facultatives.
        }
    }

    private static void ReadMmcPack(IPackFileSystem pack, PackFile file, ref string? mcVersion, ref string? loader)
    {
        try
        {
            using var doc = pack.ReadJson(file);
            if (!doc.RootElement.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Array) return;
            foreach (var component in components.EnumerateArray())
            {
                var uid = component.GetString("uid");
                var version = component.GetString("version");
                if (uid == "net.minecraft") mcVersion ??= version;
                else if (uid is "net.minecraftforge" or "net.neoforged" or "net.fabricmc.fabric-loader" or "org.quiltmc.quilt-loader")
                    loader ??= $"{component.GetString("cachedName") ?? uid} {version}".Trim();
            }
        }
        catch (JsonException)
        {
        }
    }

    [GeneratedRegex(@"^name=(.+)$", RegexOptions.Multiline)]
    private static partial Regex InstanceCfgName();

    private static string? ReadInstanceCfgName(IPackFileSystem pack, PackFile file)
    {
        using var reader = new StreamReader(pack.Open(file.Path));
        var match = InstanceCfgName().Match(reader.ReadToEnd());
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
