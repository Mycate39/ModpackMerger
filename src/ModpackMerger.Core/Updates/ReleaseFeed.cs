using System.Text.Json;
using ModpackMerger.Core.Injection;
using ModpackMerger.Core.Parsing;

namespace ModpackMerger.Core.Updates;

/// <summary>Mise à jour disponible, décrite par une release GitHub.</summary>
public sealed record UpdateInfo(string Version, string DownloadUrl, long Size, string? Sha256, string PageUrl, string? Notes);

/// <summary>Lit la réponse de l'API GitHub "releases/latest" et décide si une mise à jour est disponible.</summary>
public static class ReleaseFeed
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/Mycate39/ModpackMerger/releases/latest";

    /// <summary>
    /// Renvoie la mise à jour si la release est publiée (ni brouillon ni pre-release), plus récente que
    /// <paramref name="currentVersion"/> et fournit un installeur .exe ; sinon null.
    /// </summary>
    public static UpdateInfo? Parse(string json, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;
        if (root.GetString("tag_name") is not { } tag) return null;

        var version = tag.TrimStart('v', 'V');
        if (VersionComparer.Compare(version, currentVersion) is not > 0) return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var installer = assets.EnumerateArray().FirstOrDefault(a =>
            a.GetString("name") is { } name && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (installer.ValueKind != JsonValueKind.Object || installer.GetString("browser_download_url") is not { } url) return null;

        // GitHub fournit "digest": "sha256:<hex>" pour chaque fichier joint.
        var digest = installer.GetString("digest");
        var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
        var size = installer.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;

        return new UpdateInfo(version, url, size, sha256, root.GetString("html_url") ?? url, root.GetString("body"));
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
