using ModpackMerger.Core.Download;
using ModpackMerger.Core.Injection;
using ModpackMerger.Core.Metadata;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Tests;

public sealed class InjectionTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly string _mods;

    public InjectionTests()
    {
        _mods = _files.Path("instance", "mods");
        Directory.CreateDirectory(_mods);
    }

    public void Dispose() => _files.Dispose();

    private DownloadedMod Incoming(string fileName, string id, string version, string? provides = null)
    {
        var path = _files.Zip(System.IO.Path.Combine("cache", fileName), new Dictionary<string, string>
        {
            ["fabric.mod.json"] = provides is null
                ? $$"""{ "id": "{{id}}", "version": "{{version}}" }"""
                : $$"""{ "id": "{{id}}", "version": "{{version}}", "provides": ["{{provides}}"] }""",
        });
        return Downloaded(path, fileName);
    }

    private static DownloadedMod Downloaded(string path, string displayName)
    {
        var entry = new ModEntry { Source = ModSource.Embedded, DisplayName = displayName, FileName = System.IO.Path.GetFileName(path) };
        return new DownloadedMod(entry, path, ModDownloader.Sha1Of(path), JarMetadataReader.Read(path));
    }

    private void Installed(string fileName, string id, string version) =>
        _files.FabricJar(System.IO.Path.Combine("instance", "mods", fileName), id, version);

    private string[] ModsFolder() =>
        Directory.GetFiles(_mods).Select(f => System.IO.Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray();

    private List<PlanItem> Analyze(params DownloadedMod[] incoming) =>
        ConflictAnalyzer.Analyze(incoming, ConflictAnalyzer.ScanModsFolder(_mods));

    [Fact]
    public async Task Analyzes_conflicts_and_injects_with_backup()
    {
        Installed("alpha-1.0.jar", "alpha", "1.0");      // plus ancien que l'entrant
        Installed("beta-3.0.jar", "beta", "3.0");        // plus récent que l'entrant
        var same = Incoming("same-1.0.jar", "same", "1.0");
        File.Copy(same.LocalPath, System.IO.Path.Combine(_mods, "same-1.0.jar")); // identique

        var plan = Analyze(
            Incoming("alpha-2.0.jar", "alpha", "2.0"),
            Incoming("beta-2.0.jar", "beta", "2.0"),
            same,
            Incoming("gamma-1.0.jar", "gamma", "1.0"));
        var byId = plan.ToDictionary(p => p.Incoming.Metadata.ModId!);

        Assert.Equal(ConflictKind.IncomingNewer, byId["alpha"].Kind);
        Assert.Equal(ConflictAction.Replace, byId["alpha"].Action);
        Assert.Equal(ConflictKind.IncomingOlder, byId["beta"].Kind);
        Assert.Equal(ConflictAction.Skip, byId["beta"].Action);
        Assert.Equal(ConflictKind.Identical, byId["same"].Kind);
        Assert.Equal(ConflictKind.New, byId["gamma"].Kind);

        byId["beta"].Action = ConflictAction.KeepBoth;
        var report = await ModInjector.InjectAsync(plan, _mods, null, CancellationToken.None);

        Assert.Equal(2, report.Installed);   // gamma + beta (keep both)
        Assert.Equal(1, report.Replaced);    // alpha
        Assert.Equal(1, report.Skipped);     // same
        Assert.Empty(report.Errors);
        Assert.Equal(["alpha-2.0.jar", "beta-2.0.jar", "beta-3.0.jar", "gamma-1.0.jar", "same-1.0.jar"], ModsFolder());
        Assert.True(File.Exists(System.IO.Path.Combine(report.BackupDirectory!, "alpha-1.0.jar")));
    }

    [Fact]
    public async Task Replaces_by_mod_id_even_when_file_names_are_unrelated()
    {
        Installed("JEI_Forge_1.20.1_v15.2.0.jar", "jei", "15.2.0");

        var plan = Analyze(Incoming("jei-1.20.1-forge-15.20.0.105.jar", "jei", "15.20.0.105"));

        var item = Assert.Single(plan);
        Assert.Equal(ConflictKind.IncomingNewer, item.Kind);
        await ModInjector.InjectAsync(plan, _mods, null, CancellationToken.None);
        Assert.Equal(["jei-1.20.1-forge-15.20.0.105.jar"], ModsFolder());
    }

    [Fact]
    public async Task Replaces_every_installed_copy_of_the_mod()
    {
        // Deux versions déjà présentes par erreur, sous des noms différents.
        Installed("sodium-fabric-0.5.3.jar", "sodium", "0.5.3");
        Installed("Sodium 0.5.8.jar", "sodium", "0.5.8");
        Installed("other-1.0.jar", "other", "1.0");

        var plan = Analyze(Incoming("sodium-fabric-0.5.13+mc1.20.1.jar", "sodium", "0.5.13+mc1.20.1"));

        var item = Assert.Single(plan);
        Assert.Equal(2, item.Existing.Count);
        Assert.Equal("0.5.8", item.PrimaryExisting!.Metadata.Version); // comparaison contre la plus récente

        var report = await ModInjector.InjectAsync(plan, _mods, null, CancellationToken.None);

        Assert.Equal(["other-1.0.jar", "sodium-fabric-0.5.13+mc1.20.1.jar"], ModsFolder());
        Assert.Equal(2, Directory.GetFiles(report.BackupDirectory!).Length);
    }

    [Fact]
    public async Task Keeps_a_disabled_mod_disabled_after_replacement()
    {
        Installed("journeymap-5.9.jar.disabled", "journeymap", "5.9");

        var plan = Analyze(Incoming("journeymap-1.20.1-5.10.jar", "journeymap", "5.10"));
        await ModInjector.InjectAsync(plan, _mods, null, CancellationToken.None);

        Assert.Equal(["journeymap-1.20.1-5.10.jar.disabled"], ModsFolder());
    }

    [Fact]
    public void Matches_through_provided_ids()
    {
        Installed("rubidium-0.6.jar", "rubidium", "0.6");

        var item = Assert.Single(Analyze(Incoming("embeddium-0.3.jar", "embeddium", "0.3", provides: "rubidium")));

        Assert.Equal("rubidium-0.6.jar", item.PrimaryExisting!.FileName);
        // Mods différents (alternative compatible) : leurs numéros de version ne sont pas comparables.
        Assert.Equal(ConflictKind.Different, item.Kind);
    }

    [Fact]
    public void Does_not_confuse_mods_with_similar_names()
    {
        Installed("create-1.20.1-0.5.1.jar", "create", "0.5.1");

        var item = Assert.Single(Analyze(Incoming("createaddition-1.20.1-1.2.jar", "createaddition", "1.2")));

        Assert.Equal(ConflictKind.New, item.Kind);
    }

    [Fact]
    public async Task Matches_jars_without_metadata_by_normalized_name()
    {
        File.WriteAllText(System.IO.Path.Combine(_mods, "[1.20.1] SecurityCraft v1.9.8.jar"), "v1");
        var path = _files.Path("securitycraft-1.9.10-forge.jar");
        File.WriteAllText(path, "v2");

        var plan = Analyze(Downloaded(path, "SecurityCraft"));

        var item = Assert.Single(plan);
        Assert.Equal(ConflictKind.Different, item.Kind); // pas de version lisible : l'utilisateur tranche
        Assert.Equal(ConflictAction.Skip, item.Action);

        item.Action = ConflictAction.Replace;
        await ModInjector.InjectAsync(plan, _mods, null, CancellationToken.None);
        Assert.Equal(["securitycraft-1.9.10-forge.jar"], ModsFolder());
    }
}
