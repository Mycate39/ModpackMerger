using System.IO.Compression;
using System.Text.Json;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Parsing;

/// <summary>Point d'entrée : détecte le format de l'archive et délègue au parser adapté.</summary>
public sealed class ModpackReader
{
    // Modrinth en premier : certains .mrpack exportés par des launchers contiennent aussi un manifest.json.
    private readonly IReadOnlyList<IModpackParser> _parsers = [new ModrinthParser(), new CurseForgeParser()];

    public ModpackInfo Read(string path)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new ModpackFormatException("Le fichier n'est pas une archive ZIP valide.", ex);
        }

        using (archive)
        {
            var parser = _parsers.FirstOrDefault(p => p.CanParse(archive))
                ?? throw new ModpackFormatException(
                    "Archive non reconnue : ni manifest.json (CurseForge) ni modrinth.index.json (Modrinth) trouvé à la racine.");
            try
            {
                return parser.Parse(archive, path);
            }
            catch (JsonException ex)
            {
                throw new ModpackFormatException($"Manifeste JSON invalide : {ex.Message}", ex);
            }
        }
    }
}
