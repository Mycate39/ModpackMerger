namespace ModpackMerger.Core.Models;

public enum ModpackFormat
{
    CurseForge,
    Modrinth,

    /// <summary>Dossier classique : instance Minecraft, dossier "mods" ou liste de jars.</summary>
    Folder,
}

/// <summary>Contenu analysé d'un modpack : archive (.zip CurseForge, .mrpack Modrinth) ou dossier.</summary>
public sealed class ModpackInfo
{
    /// <summary>Chemin de l'archive ou du dossier source.</summary>
    public required string SourcePath { get; init; }
    public required ModpackFormat Format { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Author { get; init; }
    public string? MinecraftVersion { get; init; }

    /// <summary>Loader principal, ex. "forge-47.2.0", "fabric-loader 0.15.11".</summary>
    public string? Loader { get; init; }

    public required IReadOnlyList<ModEntry> Mods { get; init; }
}
