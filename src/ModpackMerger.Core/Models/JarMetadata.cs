namespace ModpackMerger.Core.Models;

public enum ModLoaderKind
{
    Unknown,
    Fabric,
    Quilt,
    Forge,
    NeoForge,
    LegacyForge,
}

/// <summary>Métadonnées lues à l'intérieur d'un .jar (fabric.mod.json, mods.toml, ...).</summary>
public sealed record JarMetadata(string? ModId, string? Name, string? Version, ModLoaderKind Loader)
{
    public static readonly JarMetadata Empty = new(null, null, null, ModLoaderKind.Unknown);

    /// <summary>
    /// Identifiants secondaires : autres [[mods]] d'un mods.toml, "provides" de Fabric/Quilt.
    /// Servent à reconnaître un même mod d'une version à l'autre, quel que soit le nom du fichier.
    /// </summary>
    public IReadOnlyList<string> AliasIds { get; init; } = [];

    public IEnumerable<string> AllIds =>
        (ModId is null ? AliasIds : AliasIds.Prepend(ModId)).Where(id => !string.IsNullOrWhiteSpace(id));
}
