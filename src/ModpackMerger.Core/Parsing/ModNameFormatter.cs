using System.Globalization;
using System.Text.RegularExpressions;

namespace ModpackMerger.Core.Parsing;

/// <summary>Déduit un nom lisible / une clé de comparaison à partir d'un nom de fichier .jar.</summary>
public static partial class ModNameFormatter
{
    // Coupe au premier séparateur suivi d'un chiffre ou d'un marqueur de loader/version :
    // "sodium-fabric-0.5.8+mc1.20.1.jar" -> "sodium", "fabric-api-0.92.2.jar" -> "fabric-api", "jei_1.20.1-forge.jar" -> "jei"
    [GeneratedRegex(@"^(.+?)(?:[-_+ ](?:v?\d|mc\d|forge|neoforge|fabric|quilt)).*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex Bracketed();

    // Segments qui ne font pas partie du nom : versions (1.20.1, v2, mc1.20, r3), loaders, canaux de publication.
    [GeneratedRegex(@"^(?:v?\d.*|mc\d.*|r\d+|forge|neoforge|fabric|quilt|universal|release|beta|alpha|snapshot)$", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseToken();

    public static string Stem(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var match = VersionSuffix().Match(name);
        return match.Success ? match.Groups[1].Value : name;
    }

    /// <summary>
    /// Clé indépendante de la version, pour rapprocher deux fichiers d'un même mod sans métadonnées :
    /// "[1.20.1] SecurityCraft v1.9.8.jar" et "SecurityCraft-1.9.10-forge.jar" -> "securitycraft".
    /// Le nom = les segments situés entre le bruit de tête (version MC…) et le premier segment de version.
    /// </summary>
    public static string NormalizedKey(string fileName)
    {
        var name = fileName;
        if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) name = name[..^".disabled".Length];
        name = Bracketed().Replace(Path.GetFileNameWithoutExtension(name), " ");

        // Le point n'est pas un séparateur : il reste à l'intérieur des segments de version ("1.20.1").
        var tokens = Regex.Split(name, @"[-_+ ]+").Where(t => t.Length > 0).ToList();
        var words = new List<string>();
        foreach (var token in tokens)
        {
            var isNoise = NoiseToken().IsMatch(token);
            // Un loader en tête fait partie du nom ("fabric-api"), ailleurs il marque la fin du nom.
            if (isNoise && (words.Count > 0 || char.IsDigit(token[0]) || token.StartsWith("mc", StringComparison.OrdinalIgnoreCase) || token[0] is 'v' or 'V'))
            {
                if (words.Count > 0) break;
                continue; // bruit de tête, ex. "1.20.1-jei-15.2.jar"
            }
            words.Add(token);
        }

        var key = string.Concat(string.Concat(words).ToLowerInvariant().Where(char.IsLetterOrDigit));
        return key.Length > 0 ? key : string.Concat(Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Where(char.IsLetterOrDigit));
    }

    public static string FromFileName(string fileName)
    {
        var words = Stem(fileName).Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0
            ? fileName
            : string.Join(' ', words.Select(w => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w)));
    }
}
