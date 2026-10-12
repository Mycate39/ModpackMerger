using System.Text.Json;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Parsing;

/// <summary>Point d'entrée : détecte le type de source (archive ou dossier) et délègue au parser adapté.</summary>
public sealed class ModpackReader
{
    // Modrinth en premier : certains .mrpack exportés par des launchers contiennent aussi un manifest.json.
    private readonly IModpackParser[] _manifestParsers = [new ModrinthParser(), new CurseForgeParser()];
    private readonly FolderParser _folderParser = new();

    public ModpackInfo Read(string path)
    {
        var isFolder = Directory.Exists(path);
        if (!isFolder && !File.Exists(path))
            throw new ModpackFormatException($"Introuvable : {path}");

        using var pack = PackFileSystem.Open(path);
        try
        {
            return SelectParser(pack, isFolder).Parse(pack, path);
        }
        catch (JsonException ex)
        {
            throw new ModpackFormatException($"Manifeste JSON invalide : {ex.Message}", ex);
        }
    }

    private IModpackParser SelectParser(IPackFileSystem pack, bool isFolder)
    {
        if (!isFolder)
        {
            return _manifestParsers.FirstOrDefault(p => p.CanParse(pack))
                ?? throw new ModpackFormatException(
                    "Archive non reconnue : ni manifest.json (CurseForge) ni modrinth.index.json (Modrinth) trouvé à la racine.");
        }

        // Dossier : les jars réellement présents dans un sous-dossier "mods" priment sur un éventuel manifest
        // (une instance installée peut garder son manifest.json d'origine alors que ses mods ont changé).
        if (FolderParser.HasModsSubfolder(pack)) return _folderParser;

        // Sinon, export décompressé (manifest + overrides), puis simple dossier de jars.
        return _manifestParsers.FirstOrDefault(p => p.CanParse(pack))
            ?? (_folderParser.CanParse(pack)
                ? _folderParser
                : throw new ModpackFormatException(
                    "Aucun mod trouvé : choisissez un dossier d'instance, son dossier « mods », ou un modpack décompressé."));
    }
}
