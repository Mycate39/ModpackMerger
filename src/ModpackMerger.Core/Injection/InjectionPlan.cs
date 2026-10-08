using ModpackMerger.Core.Download;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Injection;

/// <summary>Un jar déjà présent dans le dossier mods cible.</summary>
public sealed record InstalledMod(string Path, string Sha1, JarMetadata Metadata)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Mod désactivé par le launcher (extension .jar.disabled).</summary>
    public bool IsDisabled => Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
}

public enum ConflictKind
{
    /// <summary>Aucun équivalent dans le dossier cible.</summary>
    New,

    /// <summary>Fichier strictement identique (même SHA-1) déjà présent.</summary>
    Identical,

    IncomingNewer,
    IncomingOlder,

    /// <summary>Même version déclarée mais contenu différent (ex. build Forge vs Fabric).</summary>
    SameVersionDifferentFile,

    /// <summary>Même mod mais versions non comparables.</summary>
    Different,
}

public enum ConflictAction
{
    Install,
    Replace,
    Skip,
    KeepBoth,
}

/// <summary>
/// Décision pour un mod entrant. <see cref="Existing"/> contient TOUS les fichiers du dossier cible
/// reconnus comme le même mod (souvent un seul, mais plusieurs versions peuvent cohabiter par erreur) :
/// "Remplacer" les retire tous, quel que soit leur nom de fichier.
/// </summary>
public sealed class PlanItem(DownloadedMod incoming, IReadOnlyList<InstalledMod> existing, ConflictKind kind)
{
    public DownloadedMod Incoming { get; } = incoming;
    public IReadOnlyList<InstalledMod> Existing { get; } = existing;
    public ConflictKind Kind { get; } = kind;

    /// <summary>Fichier existant de référence pour la comparaison de versions (le plus récent).</summary>
    public InstalledMod? PrimaryExisting { get; } = existing.FirstOrDefault();

    public ConflictAction Action { get; set; } = DefaultActionFor(kind);

    public bool IsConflict => Kind is not (ConflictKind.New or ConflictKind.Identical);

    public static ConflictAction DefaultActionFor(ConflictKind kind) => kind switch
    {
        ConflictKind.New => ConflictAction.Install,
        ConflictKind.IncomingNewer => ConflictAction.Replace,
        _ => ConflictAction.Skip, // en cas de doute on ne touche pas au pack personnel
    };

    /// <summary>Actions pertinentes pour ce type d'élément (pour l'UI).</summary>
    public IReadOnlyList<ConflictAction> AvailableActions => Kind == ConflictKind.New
        ? [ConflictAction.Install, ConflictAction.Skip]
        : [ConflictAction.Replace, ConflictAction.Skip, ConflictAction.KeepBoth];
}
