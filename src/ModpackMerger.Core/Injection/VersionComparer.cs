using System.Text.RegularExpressions;

namespace ModpackMerger.Core.Injection;

/// <summary>
/// Comparaison "best effort" de versions de mods (pas de standard commun entre auteurs).
/// Découpe en segments numériques/alphabétiques ; les métadonnées de build (+...) ne départagent qu'en dernier.
/// Exemples : 1.2.10 &gt; 1.2.9 ; 1.0.0 &gt; 1.0.0-beta ; 0.5.8+mc1.20.1 == 0.5.8+mc1.20.1.
/// </summary>
public static partial class VersionComparer
{
    [GeneratedRegex(@"\d+|[a-zA-Z]+")]
    private static partial Regex Token();

    /// <returns>&lt;0 si a &lt; b, 0 si égales, &gt;0 si a &gt; b, null si non comparable.</returns>
    public static int? Compare(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return null;
        if (a.Contains("${", StringComparison.Ordinal) || b.Contains("${", StringComparison.Ordinal)) return null;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 0;

        var (coreA, buildA) = Split(a);
        var (coreB, buildB) = Split(b);
        var result = CompareCore(coreA, coreB);
        return result != 0 ? result : CompareCore(buildA, buildB);
    }

    private static (string Core, string Build) Split(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? (version, "") : (version[..plus], version[(plus + 1)..]);
    }

    private static int CompareCore(string a, string b)
    {
        var ta = Token().Matches(a).Select(m => m.Value).ToList();
        var tb = Token().Matches(b).Select(m => m.Value).ToList();

        for (var i = 0; i < Math.Max(ta.Count, tb.Count); i++)
        {
            if (i >= ta.Count) return IsPreRelease(tb[i]) ? 1 : -1;  // 1.0 vs 1.0-beta => 1.0 plus récent
            if (i >= tb.Count) return IsPreRelease(ta[i]) ? -1 : 1;

            var na = char.IsDigit(ta[i][0]);
            var nb = char.IsDigit(tb[i][0]);
            int cmp;
            if (na && nb)
            {
                cmp = CompareNumeric(ta[i], tb[i]);
            }
            else if (na != nb)
            {
                cmp = na ? 1 : -1; // un numéro l'emporte sur un suffixe textuel (1.0.1 > 1.0.beta)
            }
            else
            {
                cmp = RankText(ta[i]).CompareTo(RankText(tb[i]));
                if (cmp == 0) cmp = string.Compare(ta[i], tb[i], StringComparison.OrdinalIgnoreCase);
            }

            if (cmp != 0) return Math.Sign(cmp);
        }
        return 0;
    }

    /// <summary>Compare deux suites de chiffres de longueur arbitraire (sans dépassement).</summary>
    private static int CompareNumeric(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    }

    private static bool IsPreRelease(string token) => RankText(token) < 3;

    private static int RankText(string token) => token.ToLowerInvariant() switch
    {
        "snapshot" or "dev" or "pre" => 0,
        "alpha" or "a" => 1,
        "beta" or "b" or "rc" => 2,
        _ => 3,
    };
}
