using System.IO.Compression;
using System.Text.Json;

namespace ModpackMerger.Core.Parsing;

/// <summary>Fichier d'un modpack, quel que soit son conteneur (archive ou dossier).</summary>
public sealed record PackFile(string Path, long Length)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Vue en lecture seule sur le contenu d'un modpack : une archive (.zip/.mrpack) ou un dossier
/// (instance, dossier "mods", ou pack décompressé). Les chemins sont relatifs et utilisent '/'.
/// </summary>
public interface IPackFileSystem : IDisposable
{
    IReadOnlyList<PackFile> Files { get; }

    Stream Open(string path);
}

public static class PackFileSystem
{
    public static IPackFileSystem Open(string path) =>
        Directory.Exists(path) ? new DirectoryPackFileSystem(path) : new ZipPackFileSystem(path);

    /// <summary>Recherche insensible à la casse et aux séparateurs (certains packs utilisent '\').</summary>
    public static PackFile? Find(this IPackFileSystem fs, string path) =>
        fs.Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    public static JsonDocument ReadJson(this IPackFileSystem fs, PackFile file)
    {
        using var stream = fs.Open(file.Path);
        return JsonDocument.Parse(stream, ZipHelpers.LenientJson);
    }

    /// <summary>Jars placés directement dans "{directory}/" (sans sous-dossier). Chaîne vide = racine.</summary>
    public static IEnumerable<PackFile> JarsIn(this IPackFileSystem fs, string directory)
    {
        var prefix = directory.Trim('/') is { Length: > 0 } d ? d + "/" : "";
        return fs.Files.Where(f =>
            f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && f.Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && f.Path.IndexOf('/', prefix.Length) < 0);
    }
}

internal sealed class ZipPackFileSystem : IPackFileSystem
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public ZipPackFileSystem(string path)
    {
        try
        {
            _archive = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new ModpackFormatException("Le fichier n'est pas une archive ZIP valide.", ex);
        }

        foreach (var entry in _archive.Entries.Where(e => !e.FullName.EndsWith('/')))
            _entries.TryAdd(PackFileSystem.Normalize(entry.FullName), entry);
        Files = _entries.Select(e => new PackFile(e.Key, e.Value.Length)).ToList();
    }

    public IReadOnlyList<PackFile> Files { get; }

    public Stream Open(string path)
    {
        // ZipArchive n'est pas thread-safe et son flux n'est pas "seekable" : on renvoie une copie en mémoire.
        var buffer = new MemoryStream();
        lock (_archive)
        {
            using var stream = _entries[PackFileSystem.Normalize(path)].Open();
            stream.CopyTo(buffer);
        }
        buffer.Position = 0;
        return buffer;
    }

    public void Dispose() => _archive.Dispose();
}

internal sealed class DirectoryPackFileSystem : IPackFileSystem
{
    private readonly string _root;

    public DirectoryPackFileSystem(string root)
    {
        _root = Path.GetFullPath(root);
        // Seuls les fichiers utiles à l'analyse sont indexés (pas de parcours complet d'une grosse instance).
        var files = new List<PackFile>();
        foreach (var name in FolderParser.InstanceFiles.Concat(["manifest.json", "modrinth.index.json"]))
            AddIfExists(files, name);
        foreach (var dir in FolderParser.ModsDirectories.Concat(["overrides/mods", "client-overrides/mods"]))
        {
            var full = Path.Combine(_root, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var file in Directory.EnumerateFiles(full, "*.jar"))
                files.Add(new PackFile(Relative(file), new FileInfo(file).Length));
        }
        Files = files;
    }

    public IReadOnlyList<PackFile> Files { get; }

    public Stream Open(string path) => File.OpenRead(Path.Combine(_root, PackFileSystem.Normalize(path)));

    private void AddIfExists(List<PackFile> files, string name)
    {
        var full = Path.Combine(_root, name);
        if (File.Exists(full)) files.Add(new PackFile(name, new FileInfo(full).Length));
    }

    private string Relative(string fullPath) => PackFileSystem.Normalize(Path.GetRelativePath(_root, fullPath));

    public void Dispose() { }
}
