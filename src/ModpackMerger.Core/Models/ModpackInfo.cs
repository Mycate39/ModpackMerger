namespace ModpackMerger.Core.Models;

public enum ModpackFormat
{
    CurseForge,
    Modrinth,
}

/// <summary>Contenu analysé d'une archive de modpack (.zip CurseForge ou .mrpack Modrinth).</summary>
public sealed class ModpackInfo
{
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
