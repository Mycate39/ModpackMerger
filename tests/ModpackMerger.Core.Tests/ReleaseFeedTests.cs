using ModpackMerger.Core.Updates;

namespace ModpackMerger.Core.Tests;

public sealed class ReleaseFeedTests
{
    private static string Release(string tag, bool prerelease = false, bool draft = false, string assetName = "ModpackMerger-Setup-1.1.0.exe") => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLowerInvariant()}}, "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
          "html_url": "https://github.com/Mycate39/ModpackMerger/releases/tag/{{tag}}",
          "body": "Notes",
          "assets": [
            { "name": "{{assetName}}", "size": 1234,
              "browser_download_url": "https://github.com/Mycate39/ModpackMerger/releases/download/{{tag}}/{{assetName}}",
              "digest": "sha256:ABCDEF" }
          ]
        }
        """;

    [Fact]
    public void Detects_a_newer_release_with_its_installer()
    {
        var update = ReleaseFeed.Parse(Release("v1.1.0"), "1.0.0");

        Assert.NotNull(update);
        Assert.Equal("1.1.0", update.Version);
        Assert.EndsWith("ModpackMerger-Setup-1.1.0.exe", update.DownloadUrl);
        Assert.Equal("ABCDEF", update.Sha256);
        Assert.Equal(1234, update.Size);
        Assert.EndsWith("/tag/v1.1.0", update.PageUrl);
    }

    [Theory]
    [InlineData("v1.0.0", "1.0.0")]   // même version
    [InlineData("v0.9.0", "1.0.0")]   // plus ancienne
    [InlineData("v1.0.10", "1.0.10")]
    public void Ignores_same_or_older_versions(string tag, string current) =>
        Assert.Null(ReleaseFeed.Parse(Release(tag), current));

    [Fact]
    public void Compares_versions_numerically() =>
        Assert.NotNull(ReleaseFeed.Parse(Release("v1.0.10"), "1.0.9"));

    [Fact]
    public void Ignores_prereleases_drafts_and_releases_without_installer()
    {
        Assert.Null(ReleaseFeed.Parse(Release("v2.0.0", prerelease: true), "1.0.0"));
        Assert.Null(ReleaseFeed.Parse(Release("v2.0.0", draft: true), "1.0.0"));
        Assert.Null(ReleaseFeed.Parse(Release("v2.0.0", assetName: "ModpackMerger.zip"), "1.0.0"));
    }

    [Fact]
    public void Selects_the_asset_matching_the_filter()
    {
        static bool MacArm(string name) => name.EndsWith("-macos-arm64.zip", StringComparison.OrdinalIgnoreCase);

        var update = ReleaseFeed.Parse(Release("v1.2.0", assetName: "ModpackMerger-1.2.0-macos-arm64.zip"), "1.1.0", MacArm);
        Assert.NotNull(update);
        Assert.EndsWith("macos-arm64.zip", update.DownloadUrl);

        Assert.Null(ReleaseFeed.Parse(Release("v1.2.0"), "1.1.0", MacArm));
    }
}
