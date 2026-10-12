using ModpackMerger.Core.Download;
using ModpackMerger.Core.Models;
using ModpackMerger.Core.Parsing;
using ModpackMerger.Core.Resolution;

namespace ModpackMerger.Core.Tests;

public sealed class FolderSourceTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly ModpackReader _reader = new();

    public void Dispose() => _files.Dispose();

    private void Text(string relativePath, string content)
    {
        var path = _files.Path(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Reads_a_CurseForge_app_instance_folder()
    {
        _files.FabricJar("Mon Pack/mods/sodium-0.5.jar", "sodium", "0.5", "Sodium");
        _files.FabricJar("Mon Pack/mods/lithium-0.11.jar", "lithium", "0.11", "Lithium");
        Text("Mon Pack/mods/old-1.0.jar.disabled", "désactivé : ignoré");
        Text("Mon Pack/minecraftinstance.json",
            """{ "name": "Mon Pack", "gameVersion": "1.20.1", "baseModLoader": { "name": "fabric-0.15.11-1.20.1" } }""");
        // Manifest d'origine conservé par l'instance : ce sont les jars réellement présents qui comptent.
        Text("Mon Pack/manifest.json", """{ "manifestType": "minecraftModpack", "name": "x", "files": [ { "projectID": 1, "fileID": 2 } ] }""");

        var pack = _reader.Read(_files.Path("Mon Pack"));

        Assert.Equal(ModpackFormat.Folder, pack.Format);
        Assert.Equal("Mon Pack", pack.Name);
        Assert.Equal("1.20.1", pack.MinecraftVersion);
        Assert.Equal("fabric-0.15.11-1.20.1", pack.Loader);
        Assert.Equal(["lithium-0.11.jar", "sodium-0.5.jar"], pack.Mods.Select(m => m.FileName!).Order());
        Assert.All(pack.Mods, m => Assert.Equal(ModSource.Embedded, m.Source));
    }

    [Fact]
    public void Reads_a_Prism_instance_with_dot_minecraft_mods()
    {
        _files.FabricJar("prism/.minecraft/mods/iris-1.7.jar", "iris", "1.7");
        Text("prism/instance.cfg", "InstanceType=OneSix\nname=Survie Optimisée\n");
        Text("prism/mmc-pack.json", """
            { "components": [
                { "uid": "net.minecraft", "version": "1.21.1" },
                { "uid": "net.fabricmc.fabric-loader", "version": "0.16.5", "cachedName": "Fabric Loader" } ] }
            """);

        var pack = _reader.Read(_files.Path("prism"));

        Assert.Equal("Survie Optimisée", pack.Name);
        Assert.Equal("1.21.1", pack.MinecraftVersion);
        Assert.Equal("Fabric Loader 0.16.5", pack.Loader);
        Assert.Equal("iris-1.7.jar", Assert.Single(pack.Mods).FileName);
    }

    [Fact]
    public void Reads_a_mods_folder_directly_and_names_it_after_its_parent()
    {
        _files.FabricJar("Aventure/mods/jei-15.jar", "jei", "15");

        var pack = _reader.Read(_files.Path("Aventure", "mods"));

        Assert.Equal("Aventure", pack.Name);
        Assert.Single(pack.Mods);
    }

    [Fact]
    public void Reads_an_extracted_modrinth_pack_folder()
    {
        Text("extrait/modrinth.index.json", """
            { "game": "minecraft", "name": "Extrait", "files": [
              { "path": "mods/a.jar", "hashes": { "sha1": "aa" }, "downloads": ["https://x/a.jar"] } ] }
            """);
        _files.FabricJar("extrait/overrides/mods/local.jar", "local", "1.0");

        var pack = _reader.Read(_files.Path("extrait"));

        Assert.Equal(ModpackFormat.Modrinth, pack.Format);
        Assert.Equal(2, pack.Mods.Count);
    }

    [Fact]
    public void Rejects_a_folder_without_mods()
    {
        Text("vide/readme.txt", "rien");
        Assert.Throws<ModpackFormatException>(() => _reader.Read(_files.Path("vide")));
    }

    [Fact]
    public async Task Resolves_and_copies_jars_from_a_folder_source()
    {
        _files.FabricJar("inst/mods/sodium-0.5.jar", "sodium", "0.5", "Sodium");
        var pack = _reader.Read(_files.Path("inst"));
        using var http = new HttpClient();

        await new ModpackResolver(http, null).ResolveAsync(pack, null, CancellationToken.None);
        var result = await new ModDownloader(http, _files.Path("cache")).DownloadAsync(pack, pack.Mods, null, CancellationToken.None);

        Assert.Equal("Sodium", pack.Mods[0].DisplayName);
        var downloaded = Assert.Single(result.Downloaded);
        Assert.Equal("sodium", downloaded.Metadata.ModId);
        Assert.StartsWith(_files.Path("cache"), downloaded.LocalPath);
    }
}
