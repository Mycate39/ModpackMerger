using System.IO.Compression;
using System.Text;

namespace ModpackMerger.Core.Tests;

/// <summary>Fabrique des archives/jars de test dans un dossier temporaire supprimé au Dispose.</summary>
public sealed class TestFiles : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("modpackmerger-tests-").FullName;

    public string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    public string Zip(string relativePath, IDictionary<string, string> entries)
    {
        var path = Path(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
            writer.Write(content);
        }
        return path;
    }

    public string FabricJar(string relativePath, string id, string version, string? name = null) =>
        Zip(relativePath, new Dictionary<string, string>
        {
            ["fabric.mod.json"] = $$"""{ "schemaVersion": 1, "id": "{{id}}", "version": "{{version}}", "name": "{{name ?? id}}" }""",
        });

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}
