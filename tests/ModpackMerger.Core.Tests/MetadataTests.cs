using ModpackMerger.Core.Injection;
using ModpackMerger.Core.Metadata;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Tests;

public sealed class MetadataTests : IDisposable
{
    private readonly TestFiles _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public void Reads_fabric_mod_json()
    {
        var jar = _files.Zip("a.jar", new Dictionary<string, string>
        {
            ["fabric.mod.json"] = """{ "id": "embeddium", "name": "Embeddium", "version": "0.3.18", "provides": ["rubidium"] }""",
        });

        var meta = JarMetadataReader.Read(jar);

        Assert.Equal(("embeddium", "Embeddium", "0.3.18", ModLoaderKind.Fabric), (meta.ModId, meta.Name, meta.Version, meta.Loader));
        Assert.Equal(["embeddium", "rubidium"], meta.AllIds);
    }

    [Fact]
    public void Reads_all_mod_ids_of_a_multi_mod_toml()
    {
        var jar = _files.Zip("multi.jar", new Dictionary<string, string>
        {
            ["META-INF/neoforge.mods.toml"] = """
            [[mods]]
            modId="mainmod"
            version="2.0"
            [[dependencies.mainmod]]
            modId="neoforge"
            [[mods]]
            modId="mainmod_compat"
            version="2.0"
            """,
        });

        var meta = JarMetadataReader.Read(jar);

        Assert.Equal(ModLoaderKind.NeoForge, meta.Loader);
        Assert.Equal(["mainmod", "mainmod_compat"], meta.AllIds);
    }

    [Fact]
    public void Reads_forge_mods_toml_with_manifest_version()
    {
        var jar = _files.Zip("forge.jar", new Dictionary<string, string>
        {
            ["META-INF/mods.toml"] = """
            modLoader="javafml"
            loaderVersion="[47,)"
            license='MIT'
            [[mods]] #mandatory
            modId="jei"
            version="${file.jarVersion}"
            displayName="Just Enough Items"
            description='''
            Multi-line description with modId="fake"
            '''
            [[dependencies.jei]]
                modId="forge"
            """,
            ["META-INF/MANIFEST.MF"] = "Manifest-Version: 1.0\r\nImplementation-Version: 15.20.0.105\r\n",
        });

        var meta = JarMetadataReader.Read(jar);

        Assert.Equal("jei", meta.ModId);
        Assert.Equal("Just Enough Items", meta.Name);
        Assert.Equal("15.20.0.105", meta.Version);
        Assert.Equal(ModLoaderKind.Forge, meta.Loader);
    }

    [Fact]
    public void Reads_legacy_mcmod_info()
    {
        var jar = _files.Zip("old.jar", new Dictionary<string, string>
        {
            ["mcmod.info"] = """[{ "modid": "oldmod", "name": "Old Mod", "version": "1.7.10-2.0" }]""",
        });
        Assert.Equal("oldmod", JarMetadataReader.Read(jar).ModId);
    }

    [Fact]
    public void Unreadable_jar_returns_empty()
    {
        var path = _files.Path("broken.jar");
        File.WriteAllText(path, "not a zip");
        Assert.Equal(JarMetadata.Empty, JarMetadataReader.Read(path));
    }

    [Theory]
    [InlineData("1.2.10", "1.2.9", 1)]
    [InlineData("1.0.0", "1.0.0-beta", 1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("0.5.8+mc1.20.1", "0.5.8+mc1.20.1", 0)]
    [InlineData("1.20.1-0.5.1.f", "1.20.1-0.5.1.e", 1)]
    [InlineData("2.0", "10.0", -1)]
    [InlineData("12345678901234567890123456789012", "12345678901234567890123456789013", -1)]
    public void Compares_versions(string a, string b, int expected) =>
        Assert.Equal(expected, VersionComparer.Compare(a, b));

    [Theory]
    [InlineData(null, "1.0")]
    [InlineData("${file.jarVersion}", "1.0")]
    public void Unknown_versions_are_not_comparable(string? a, string b) =>
        Assert.Null(VersionComparer.Compare(a, b));
}
