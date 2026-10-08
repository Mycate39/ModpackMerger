using ModpackMerger.Core.Download;
using ModpackMerger.Core.Metadata;
using ModpackMerger.Core.Parsing;

namespace ModpackMerger.Core.Injection;

public static class ConflictAnalyzer
{
    /// <summary>Inventorie les .jar du dossier cible (les .jar.disabled comptent aussi comme présents).</summary>
    public static List<InstalledMod> ScanModsFolder(string modsDirectory)
    {
        if (!Directory.Exists(modsDirectory)) return [];
        return Directory.EnumerateFiles(modsDirectory)
            .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            .Select(f => new InstalledMod(f, ModDownloader.Sha1Of(f), JarMetadataReader.Read(f)))
            .ToList();
    }

    /// <summary>
    /// Associe chaque mod entrant aux fichiers installés qui correspondent au même mod.
    /// Le nom de fichier n'est PAS le critère principal (il change d'une version à l'autre) :
    /// 1. SHA-1 identique → déjà présent ;
    /// 2. identifiant de mod commun (modId principal, autres [[mods]] du mods.toml, "provides" Fabric/Quilt) ;
    /// 3. à défaut, même nom de fichier ;
    /// 4. à défaut, même nom normalisé sans versions/loader — seulement si un des jars n'a pas de modId,
    ///    pour ne pas confondre deux mods distincts aux noms proches.
    /// </summary>
    public static List<PlanItem> Analyze(IEnumerable<DownloadedMod> incoming, IReadOnlyList<InstalledMod> installed)
    {
        var plan = new List<PlanItem>();
        foreach (var mod in incoming.OrderBy(m => m.Entry.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var identical = installed.Where(i => i.Sha1.Equals(mod.Sha1, StringComparison.OrdinalIgnoreCase)).ToList();
            if (identical.Count > 0)
            {
                plan.Add(new PlanItem(mod, identical, ConflictKind.Identical));
                continue;
            }

            var matches = FindMatches(mod, installed)
                .OrderByDescending(i => i, Comparer<InstalledMod>.Create((a, b) => VersionComparer.Compare(a.Metadata.Version, b.Metadata.Version) ?? 0))
                .ToList();
            plan.Add(new PlanItem(mod, matches, matches.Count == 0 ? ConflictKind.New : Classify(mod, matches[0])));
        }
        return plan;
    }

    private static List<InstalledMod> FindMatches(DownloadedMod mod, IReadOnlyList<InstalledMod> installed)
    {
        var ids = mod.Metadata.AllIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var byId = installed.Where(i => i.Metadata.AllIds.Any(ids.Contains)).ToList();
        if (byId.Count > 0) return byId;

        var byName = installed.Where(i => string.Equals(StripDisabled(i.FileName), mod.FileName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count > 0) return byName;

        var key = ModNameFormatter.NormalizedKey(mod.FileName);
        return installed
            .Where(i => (ids.Count == 0 || i.Metadata.ModId is null)
                        && ModNameFormatter.NormalizedKey(i.FileName) == key)
            .ToList();
    }

    private static ConflictKind Classify(DownloadedMod mod, InstalledMod existing)
    {
        // Rapprochés via un alias ("provides") : deux mods distincts, leurs versions ne se comparent pas.
        if (mod.Metadata.ModId is { } a && existing.Metadata.ModId is { } b && !a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return ConflictKind.Different;

        return VersionComparer.Compare(mod.Metadata.Version, existing.Metadata.Version) switch
        {
            > 0 => ConflictKind.IncomingNewer,
            < 0 => ConflictKind.IncomingOlder,
            0 => ConflictKind.SameVersionDifferentFile,
            null => ConflictKind.Different,
        };
    }

    private static string StripDisabled(string fileName) =>
        fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? fileName[..^".disabled".Length] : fileName;
}
