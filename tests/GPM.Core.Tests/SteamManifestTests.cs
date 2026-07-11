using GPM.Core.Detection.Steam;

namespace GPM.Core.Tests;

public class SteamManifestTests
{
    private static string Fixture(string rel) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", rel);

    [Fact]
    public void vdf_parser_handles_nesting_and_escapes()
    {
        var root = VdfParser.Parse("""
            "outer"
            {
                "key"   "va\\lue"
                "inner" { "a" "1" }
            }
            """);
        var outer = (Dictionary<string, object>)root["outer"];
        Assert.Equal(@"va\lue", outer["key"]);
        Assert.Equal("1", ((Dictionary<string, object>)outer["inner"])["a"]);
    }

    [Fact]
    public void library_paths_come_from_fixture()
    {
        var paths = SteamManifests.LibraryPaths(File.ReadAllText(Fixture(@"steam\libraryfolders.vdf")));
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], paths);
    }

    [Fact]
    public void appmanifest_parses_fixture()
    {
        var app = SteamManifests.ParseAppManifest(File.ReadAllText(Fixture(@"steam\appmanifest_1245620.acf")));
        Assert.Equal(("1245620", "ELDEN RING", "ELDEN RING"), app);
    }

    [Fact]
    public void malformed_manifest_returns_null()
    {
        Assert.Null(SteamManifests.ParseAppManifest("\"AppState\"\n{\n\t\"appid\"\t\t\"1\"\n}"));
    }

    [Theory]
    // Truncated mid-string, mid-key, mid-value, and unterminated block: a
    // partially-written .acf (common while Steam is mid-download/update) must
    // never throw — it returns null so ScanRoot skips only that one file.
    [InlineData("\"AppState\"\n{\n\t\"appid\"\t\t\"124")]
    [InlineData("\"AppState\"\n{\n\t\"appid")]
    [InlineData("\"AppState\"\n{\n\t\"appid\"")]
    [InlineData("\"AppState\"\n{\n")]
    [InlineData("")]
    public void truncated_manifest_returns_null_without_throwing(string acfText)
    {
        Assert.Null(SteamManifests.ParseAppManifest(acfText));
    }

    [Fact]
    public void vdf_parser_does_not_throw_on_truncated_input()
    {
        // Parser must degrade gracefully rather than throw IndexOutOfRangeException.
        var root = VdfParser.Parse("\"outer\"\n{\n\t\"key\"\t\t\"val");
        var outer = (Dictionary<string, object>)root["outer"];
        Assert.Equal("val", outer["key"]);
    }
}
