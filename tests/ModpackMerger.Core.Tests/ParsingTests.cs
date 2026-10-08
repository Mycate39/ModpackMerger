using ModpackMerger.Core.Models;
using ModpackMerger.Core.Parsing;

namespace ModpackMerger.Core.Tests;

public sealed class ParsingTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly ModpackReader _reader = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public void Reads_Modrinth_pack_mods_only_plus_overrides()
    {
        var path = _files.Zip("pack.mrpack", new Dictionary<string, string>
        {
            ["modrinth.index.json"] = """
            {
              "formatVersion": 1, "game": "minecraft", "versionId": "2.1", "name": "Test Pack",
              "files": [
                { "path": "mods/sodium-fabric-0.5.8.jar", "hashes": { "sha1": "aaa", "sha512": "bbb" },
                  "downloads": ["https://cdn.modrinth.com/sodium.jar"], "fileSize": 42,
                  "env": { "client": "required", "server": "unsupported" } },
                { "path": "mods/servercore-1.0.jar", "hashes": { "sha1": "ccc" }, "downloads": ["https://x/y.jar"],
                  "env": { "client": "unsupported", "server": "required" } },
                { "path": "resourcepacks/faithful.zip", "hashes": { "sha1": "ddd" }, "downloads": ["https://x/z.zip"] }
              ],
              "dependencies": { "minecraft": "1.20.1", "fabric-loader": "0.15.11" }
            }
            """,
            ["overrides/mods/custom-mod-1.0.jar"] = "jar",
            ["client-overrides/mods/custom-mod-1.0.jar"] = "jar2",
            ["overrides/config/sodium.json"] = "{}",
        });

        var pack = _reader.Read(path);

        Assert.Equal(ModpackFormat.Modrinth, pack.Format);
        Assert.Equal("Test Pack", pack.Name);
        Assert.Equal("1.20.1", pack.MinecraftVersion);
        Assert.Equal("fabric-loader 0.15.11", pack.Loader);
        Assert.Equal(3, pack.Mods.Count);

        var sodium = pack.Mods.Single(m => m.FileName == "sodium-fabric-0.5.8.jar");
        Assert.Equal("aaa", sodium.Sha1);
        Assert.Equal(42, sodium.FileSize);
        Assert.Equal("Sodium", sodium.DisplayName);
        Assert.False(pack.Mods.Single(m => m.FileName == "servercore-1.0.jar").ClientSupported);

        var embedded = pack.Mods.Single(m => m.Source == ModSource.Embedded);
        Assert.Equal("client-overrides/mods/custom-mod-1.0.jar", embedded.ArchiveEntryPath);
    }

    [Fact]
    public void Reads_CurseForge_manifest()
    {
        var path = _files.Zip("pack.zip", new Dictionary<string, string>
        {
            ["manifest.json"] = """
            {
              "minecraft": { "version": "1.20.1", "modLoaders": [ { "id": "forge-47.2.0", "primary": true } ] },
              "manifestType": "minecraftModpack", "manifestVersion": 1,
              "name": "CF Pack", "version": "1.0", "author": "someone",
              "files": [
                { "projectID": 238222, "fileID": 5846810, "required": true },
                { "projectID": 1, "fileID": 2, "required": false }
              ],
              "overrides": "overrides"
            }
            """,
            ["overrides/mods/local.jar"] = "jar",
        });

        var pack = _reader.Read(path);

        Assert.Equal(ModpackFormat.CurseForge, pack.Format);
        Assert.Equal("forge-47.2.0", pack.Loader);
        Assert.Equal(3, pack.Mods.Count);
        var jei = pack.Mods.First();
        Assert.Equal(238222, jei.ProjectId);
        Assert.Equal("cf-238222-5846810", jei.Key);
        Assert.False(pack.Mods[1].Required);
        Assert.Equal(ModSource.Embedded, pack.Mods[2].Source);
    }

    [Fact]
    public void Rejects_unknown_archive()
    {
        var path = _files.Zip("random.zip", new Dictionary<string, string> { ["readme.txt"] = "hi" });
        Assert.Throws<ModpackFormatException>(() => _reader.Read(path));
    }

    [Fact]
    public void Rejects_non_zip_file()
    {
        var path = _files.Path("notazip.zip");
        File.WriteAllText(path, "hello");
        Assert.Throws<ModpackFormatException>(() => _reader.Read(path));
    }

    [Theory]
    [InlineData("sodium-fabric-0.5.8+mc1.20.1.jar", "sodium")]
    [InlineData("fabric-api-0.92.2+1.20.1.jar", "fabricapi")]
    [InlineData("jei-1.20.1-forge-15.20.0.105.jar", "jei")]
    [InlineData("Create_1.20.1_v0.5.1.jar", "create")]
    [InlineData("JustAMod.jar", "justamod")]
    [InlineData("[1.20.1] SecurityCraft v1.9.8.jar", "securitycraft")]
    [InlineData("securitycraft-1.9.10-forge.jar", "securitycraft")]
    [InlineData("1.20.1-jei-15.2.jar", "jei")]
    [InlineData("appleskin-fabric-mc1.20.1-2.5.1.jar", "appleskin")]
    [InlineData("Xaeros_Minimap_24.0.3_Forge_1.20.jar", "xaerosminimap")]
    [InlineData("library-1.0.jar.disabled", "library")]
    public void Normalizes_file_names(string fileName, string expected) =>
        Assert.Equal(expected, ModNameFormatter.NormalizedKey(fileName));
}
