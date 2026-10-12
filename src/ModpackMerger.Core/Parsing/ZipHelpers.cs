using System.IO.Compression;
using System.Text.Json;

namespace ModpackMerger.Core.Parsing;

/// <summary>Lecture des fichiers à l'intérieur d'un jar (métadonnées de mods).</summary>
internal static class ZipHelpers
{
    public static readonly JsonDocumentOptions LenientJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Recherche insensible à la casse et aux séparateurs (certains packs utilisent '\').</summary>
    public static ZipArchiveEntry? Find(ZipArchive archive, string path) =>
        archive.Entries.FirstOrDefault(e =>
            string.Equals(Normalize(e.FullName), path, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string entryPath) => entryPath.Replace('\\', '/').TrimStart('/');

    public static JsonDocument ReadJson(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return JsonDocument.Parse(stream, LenientJson);
    }

    public static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    public static string? GetString(this JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
