namespace ModpackMerger.Core.Models;

public enum ModSource
{
    /// <summary>Référencé par projectID/fileID dans manifest.json, à télécharger.</summary>
    CurseForge,

    /// <summary>Référencé avec URL(s) + hashes dans modrinth.index.json, à télécharger.</summary>
    Modrinth,

    /// <summary>Jar embarqué directement dans l'archive (dossier overrides/mods).</summary>
    Embedded,
}

/// <summary>
/// Un mod listé dans un modpack. Mutable sur les champs que la résolution en ligne
/// (API CurseForge / Modrinth) vient compléter après le parsing.
/// </summary>
public sealed class ModEntry
{
    public required ModSource Source { get; init; }

    /// <summary>Nom affiché. Valeur provisoire après parsing, remplacée par la résolution en ligne.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Nom du fichier .jar (connu d'emblée pour Modrinth/Embedded, après résolution pour CurseForge).</summary>
    public string? FileName { get; set; }

    public string? VersionLabel { get; set; }

    /// <summary>Faux pour les mods "optionnels" (CurseForge required=false, Modrinth env optional).</summary>
    public bool Required { get; init; } = true;

    /// <summary>Faux si le mod ne s'installe pas côté client (Modrinth env.client = unsupported).</summary>
    public bool ClientSupported { get; init; } = true;

    // --- CurseForge ---
    public int? ProjectId { get; init; }
    public int? FileId { get; init; }

    // --- Modrinth / CurseForge résolu ---
    public List<string> DownloadUrls { get; init; } = [];
    public string? Sha1 { get; set; }
    public string? Sha512 { get; init; }
    public long? FileSize { get; set; }

    // --- Embedded ---
    /// <summary>Chemin de l'entrée dans l'archive du modpack.</summary>
    public string? ArchiveEntryPath { get; init; }

    /// <summary>Clé unique et stable dans le pack (sert de nom de cache).</summary>
    public string Key => Source switch
    {
        ModSource.CurseForge => $"cf-{ProjectId}-{FileId}",
        ModSource.Modrinth => $"mr-{Sha1 ?? Sha512 ?? FileName}",
        _ => $"emb-{ArchiveEntryPath}",
    };

    public override string ToString() => DisplayName;
}
