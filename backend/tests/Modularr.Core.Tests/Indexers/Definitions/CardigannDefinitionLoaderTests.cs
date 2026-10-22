// Behavior from Prowlarr IndexerDefinitionUpdateService (deserializer + CleanIndexerDefinition); fixtures from Prowlarr/Indexers definitions v11/v12.
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Tests.Indexers.Definitions;

public sealed class CardigannDefinitionLoaderTests
{
    internal const string Minimal = """
        id: test
        name: Test
        description: Test indexer
        language: en-US
        type: public
        links:
          - https://test.example/
          - https://mirror.example/
        legacylinks:
          - https://old.example/
        caps:
          modes:
            search: [q]
        search:
          path: browse.php
          rows:
            selector: tr
          fields:
            title:
              selector: a
        """;

    internal static CardigannDefinition LoadFixture(string name) =>
        CardigannDefinitionLoader.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Indexers", "Definitions", "Fixtures", name)));

    [Fact]
    public void LoadsPublicDefinitionWithLegacyLinks()
    {
        CardigannDefinition definition = LoadFixture("1337x.yml");

        Assert.Equal("1337x", definition.Id);
        Assert.Equal("1337x", definition.Name);
        Assert.Equal("en-US", definition.Language);
        Assert.Equal(IndexerPrivacy.Public, definition.Privacy);
        Assert.Equal(3, definition.RequestDelay);
        Assert.Equal(["https://1337x.to/", "https://1337x.st/", "https://x1337x.ws/", "https://x1337x.eu/", "https://x1337x.cc/"], definition.Links);
        Assert.Equal(13, definition.LegacyLinks.Count);
        Assert.Equal("https://1337x.is/", definition.LegacyLinks[0]);
        Assert.Equal("https://x1337x.se/", definition.LegacyLinks[^1]);
        Assert.True(definition.Caps.AllowRawSearch);
        Assert.Equal(["q", "season", "ep"], definition.Caps.Modes["tv-search"]);
        Assert.Contains(definition.Caps.CategoryMappings!, mapping => mapping is { Id: "28", Cat: "TV/Anime", Desc: "Anime/Anime" });
        Assert.NotEmpty(definition.Search.Paths);
        Assert.Contains(definition.Search.Fields, field => field.Name == "title");
    }

    [Fact]
    public void LoadsPrivateDefinitionWithCaptchaLogin()
    {
        CardigannDefinition definition = LoadFixture("shakaw.yml");

        Assert.Equal(IndexerPrivacy.Private, definition.Privacy);
        Assert.Equal("pt-BR", definition.Language);
        Assert.Equal(["https://tracker.shakaw.com.br/"], definition.Links);
        Assert.Empty(definition.LegacyLinks);
        Assert.Equal(["username", "password", "freeleech"], definition.Settings.Select(setting => setting.Name));
        Assert.Equal("false", definition.Settings[2].Default);
        LoginBlock login = definition.Login!;
        Assert.Equal("form", login.Method);
        Assert.Equal("pagina_inicial.php?acao=login", login.SubmitPath);
        Assert.Equal(new CaptchaBlock { Type = "image", Selector = "img[alt=\"Carregando captcha...\"]", Input = "captcha" }, login.Captcha);
        Assert.Equal("div#div_aviso > p", login.Error![0].Message!.Selector);
        Assert.Equal("a[href=\"sair.php\"]", login.Test!.Selector);
        Assert.Equal("table#tabela_torrents > tbody > tr", definition.Search.Rows.Selector);
        SelectorBlock date = definition.Search.Fields.Single(field => field.Name == "date").Block;
        Assert.Equal(["trim", "replace", "re_replace", "append", "dateparse"], date.Filters!.Select(filter => filter.Name));
        Assert.Equal("HH:mm dd/MM/yyyy zzz", date.Filters![4].Args);
        Assert.Equal("0", definition.Search.Fields.Single(field => field.Name == "downloadvolumefactor").Block.Case!["span.icone_golden_torrent"]);
    }

    [Fact]
    public void LoadsSemiPrivateDefinitionWithCustomEncoding()
    {
        CardigannDefinition definition = LoadFixture("traht.yml");

        Assert.Equal(IndexerPrivacy.SemiPrivate, definition.Privacy);
        Assert.Equal("windows-1251", definition.Encoding);
        Assert.Equal("post", definition.Login!.Method);
        Assert.Equal("XXX", definition.Caps.Categories!["xxx"]);
        Assert.Equal("0", definition.Search.Inputs!["cat"]);
        Assert.Equal(["https://traht.org/"], definition.Links);
        Assert.Equal(["http://traht.org/"], definition.LegacyLinks);
    }

    [Fact]
    public void LoadsSchemaV12Definition()
    {
        CardigannDefinition definition = LoadFixture("blueroms.yml");

        Assert.Equal("blueroms", definition.Id);
        Assert.Equal(IndexerPrivacy.Public, definition.Privacy);
        Assert.Equal("https://www.blueroms.ws/", definition.Links[0]);
    }

    [Fact]
    public void AppliesProwlarrDefaults()
    {
        CardigannDefinition definition = CardigannDefinitionLoader.Load(Minimal);

        Assert.Equal("UTF-8", definition.Encoding);
        Assert.Same(CardigannDefinition.DefaultSettings, definition.Settings);
        Assert.True(definition.TestLinkTorrent);
        Assert.False(definition.FollowRedirect);
        Assert.Null(definition.Login);
        Assert.Null(definition.RequestDelay);
    }

    [Fact]
    public void ConvertsSingleSearchPathToPaths()
    {
        SearchPathBlock path = Assert.Single(CardigannDefinitionLoader.Load(Minimal).Search.Paths);

        Assert.Equal("browse.php", path.Path);
        Assert.True(path.InheritInputs);
        Assert.Equal("&", path.QuerySeparator);
    }

    [Fact]
    public void AppendsSingleSearchPathAfterPaths()
    {
        string yaml = Minimal.Replace("  path: browse.php", "  path: browse.php\n  paths:\n    - path: first.php\n      inheritinputs: false", StringComparison.Ordinal);

        Assert.Equal([("first.php", false), ("browse.php", true)], CardigannDefinitionLoader.Load(yaml).Search.Paths.Select(path => (path.Path, path.InheritInputs)));
    }

    [Fact]
    public void DefaultsLoginMethodToForm()
    {
        CardigannDefinition definition = CardigannDefinitionLoader.Load(Minimal + "\nlogin:\n  path: login.php\n");

        Assert.Equal("form", definition.Login!.Method);
    }

    [Fact]
    public void KeepsRepeatedSearchFieldsInOrder()
    {
        string yaml = Minimal + """

                title:
                  selector: b
                  optional: true
                size:
                  text: 1
            """;

        CardigannDefinition definition = CardigannDefinitionLoader.Load(yaml);

        Assert.Equal(["title", "title", "size"], definition.Search.Fields.Select(field => field.Name));
        Assert.Equal("b", definition.Search.Fields[1].Block.Selector);
        Assert.True(definition.Search.Fields[1].Block.Optional);
    }

    [Fact]
    public void IgnoresUnknownKeysAndReadsLowercaseKeys()
    {
        string yaml = Minimal + "\nreplaces:\n  - other\nsomethingnew: 1\ntestlinktorrent: false\nfollowredirect: true\nrequestDelay: 2.5\n";

        CardigannDefinition definition = CardigannDefinitionLoader.Load(yaml);

        Assert.False(definition.TestLinkTorrent);
        Assert.True(definition.FollowRedirect);
        Assert.Equal(2.5, definition.RequestDelay);
    }

    [Theory]
    [InlineData("")]
    [InlineData("---\n")]
    [InlineData("just a string")]
    [InlineData("- a\n- list")]
    [InlineData("id: [unclosed")]
    [InlineData("<html><body>404</body></html>")]
    public void RejectsGarbage(string yaml)
    {
        Assert.Throws<InvalidDataException>(() => CardigannDefinitionLoader.Load(yaml));
    }

    [Theory]
    [InlineData("id: test\n", "")]
    [InlineData("name: Test\n", "")]
    [InlineData("name: Test\n", "name: \"  \"\n")]
    [InlineData("description: Test indexer\n", "")]
    [InlineData("language: en-US\n", "")]
    [InlineData("type: public\n", "")]
    [InlineData("type: public\n", "type: secret\n")]
    [InlineData("  - https://test.example/\n  - https://mirror.example/\n", "")]
    [InlineData("https://test.example/", "test.example")]
    [InlineData("https://mirror.example/", "ftp://mirror.example/")]
    [InlineData("https://old.example/", "/old")]
    [InlineData("    search: [q]\n", "")]
    [InlineData("  rows:\n    selector: tr\n", "")]
    [InlineData("  fields:\n    title:\n      selector: a", "")]
    [InlineData("type: public\n", "type: public\nrequestDelay: soon\n")]
    [InlineData("type: public\n", "type: public\nsettings:\n  - label: no name\n")]
    public void RejectsInvalidDefinition(string remove, string replacement)
    {
        string yaml = Minimal.Replace(remove, replacement, StringComparison.Ordinal);

        Assert.NotEqual(Minimal, yaml);
        Assert.Throws<InvalidDataException>(() => CardigannDefinitionLoader.Load(yaml));
    }

    [Fact]
    public void ParsesEveryUpstreamDefinition()
    {
        string? root = Environment.GetEnvironmentVariable("MODULARR_INDEXERS_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(root) || !Directory.Exists(root), "Set MODULARR_INDEXERS_DIR to a Prowlarr/Indexers checkout to run this test.");

        // Prowlarr requests schema v12 and the server falls back to v11 for definitions without a v12 file.
        string[] files = [.. ((string[])["v11", "v12"]).SelectMany(version => Directory.GetFiles(Path.Combine(root, "definitions", version), "*.yml"))];
        List<string> failures = [];
        foreach (string file in files)
        {
            try
            {
                CardigannDefinitionLoader.Load(File.ReadAllText(file));
            }
            catch (InvalidDataException ex)
            {
                failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        Assert.True(files.Length > 500, $"Only {files.Length} definitions found under {root}.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
