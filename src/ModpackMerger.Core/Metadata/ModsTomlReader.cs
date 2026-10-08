namespace ModpackMerger.Core.Metadata;

/// <summary>
/// Lecteur TOML minimal, limité à ce dont on a besoin dans mods.toml :
/// les paires clé = "valeur" simples de chaque table [[mods]].
/// Les chaînes multilignes (''' / """) sont sautées.
/// </summary>
internal static class ModsTomlReader
{
    public static List<Dictionary<string, string>> ReadMods(string toml)
    {
        var mods = new List<Dictionary<string, string>>();
        Dictionary<string, string>? current = null;
        string? multilineDelimiter = null;

        foreach (var rawLine in toml.Split('\n'))
        {
            var line = rawLine.Trim();

            if (multilineDelimiter is not null)
            {
                if (line.Contains(multilineDelimiter, StringComparison.Ordinal)) multilineDelimiter = null;
                continue;
            }

            if (line.Length == 0 || line[0] == '#') continue;

            if (line[0] == '[')
            {
                current = null;
                if (line.StartsWith("[[mods]]", StringComparison.Ordinal))
                {
                    current = new Dictionary<string, string>(StringComparer.Ordinal);
                    mods.Add(current);
                }
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim().Trim('"');
            var value = line[(eq + 1)..].Trim();

            foreach (var delimiter in new[] { "'''", "\"\"\"" })
            {
                if (value.StartsWith(delimiter, StringComparison.Ordinal))
                {
                    if (value.IndexOf(delimiter, 3, StringComparison.Ordinal) < 0) multilineDelimiter = delimiter;
                    value = "";
                    break;
                }
            }

            if (current is not null && ParseString(value) is { } parsed)
                current[key] = parsed;
        }

        return mods.Where(m => m.ContainsKey("modId")).ToList();
    }

    private static string? ParseString(string value)
    {
        if (value.Length < 2) return null;
        var quote = value[0];
        if (quote != '"' && quote != '\'') return null;
        var end = value.IndexOf(quote, 1);
        return end < 0 ? null : value[1..end];
    }
}
