using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Tests.Indexers.Catalog;

public sealed class IndexerCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Directory.CreateTempSubdirectory("modularr-catalog-").FullName, "indexers");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_directory)!, recursive: true);

    private IndexerCatalog Load(IReadOnlyDictionary<string, string> files, params string[] disabled)
    {
        CatalogTestFiles.Write(_directory, files);
        return IndexerCatalog.Load(new IndexerOptions { DefinitionsDirectory = _directory, Disabled = disabled });
    }

    private static Dictionary<string, string> RealFixtures() => new()
    {
        ["1337x.yml"] = CatalogTestFiles.Fixture("1337x.yml"),
        ["blueroms.yml"] = CatalogTestFiles.Fixture("blueroms.yml"),
        ["shakaw.yml"] = CatalogTestFiles.Fixture("shakaw.yml"),
        ["traht.yml"] = CatalogTestFiles.Fixture("traht.yml"),
        ["semivideo.yml"] = CatalogTestFiles.SemiPrivateVideo,
    };

    [Fact]
    public void EnablesVideoIndexersThatNeedNoLoginAndListsTheOthersDisabled()
    {
        IndexerCatalog catalog = Load(RealFixtures());

        Assert.Empty(catalog.Errors);
        Assert.Equal(["1337x", "semivideo", "shakaw"], catalog.Entries.Select(entry => entry.Id));
        Assert.True(catalog.Find("1337x")!.Enabled);
        Assert.False(catalog.Find("shakaw")!.Enabled);

        // Semi-private, but its templates read no credential: searched like a public site.
        Assert.True(catalog.Find("semivideo")!.Enabled);
        Assert.Equal(IndexerPrivacy.Private, catalog.Find("shakaw")!.Privacy);
        Assert.Equal(IndexerPrivacy.SemiPrivate, catalog.Find("semivideo")!.Privacy);
    }

    [Fact]
    public void ADefinitionWithANullCategoryDoesNotStopTheCatalog()
    {
        // Run-2 audit: a null caps.categories value used to throw out of Load and abort startup.
        string nullCategory = CatalogTestFiles.SemiPrivateVideo
            .Replace("id: semivideo", "id: nullcat", StringComparison.Ordinal)
            .Replace("categorymappings:\n    - {id: 1, cat: Movies/HD, desc: \"Films\"}", "categories:\n    \"1\": ~", StringComparison.Ordinal);

        IndexerCatalog catalog = Load(new Dictionary<string, string> { ["a.yml"] = CatalogTestFiles.Fixture("1337x.yml"), ["z.yml"] = nullCategory });

        Assert.NotNull(catalog.Find("1337x"));
    }

    [Theory]
    [InlineData("blueroms")] // public, consoles only
    [InlineData("traht")] // semi-private, XXX only
    public void LeavesOutIndexersWithoutMovieOrTvCategories(string id)
    {
        Assert.Null(Load(RealFixtures()).Find(id));
    }

    [Fact]
    public void LeavesOutAdultIndexersWithOnlyAVideoCategory()
    {
        // e-hentai maps everything to TV/Anime.
        Assert.Null(Load(new Dictionary<string, string> { ["ehentai.yml"] = CatalogTestFiles.Fixture("ehentai.yml") }).Find("ehentai"));
    }

    [Theory]
    [InlineData("TV", "TV", true)]
    [InlineData("tv/anime", "TV", true)]
    [InlineData("TVShows", "TV", false)]
    [InlineData("Movies/HD", "TV", false)]
    [InlineData("Movies/HD", "Movies", true)]
    public void CategoryBelongsToItsRoot(string category, string root, bool expected)
    {
        Assert.Equal(expected, IndexerCatalog.IsCategoryOf(category, root));
    }

    [Fact]
    public void ExposesTheEntryDetails()
    {
        IndexerCatalogEntry entry = Load(RealFixtures()).Find("1337x")!;

        Assert.Equal("1337x", entry.Name);
        Assert.Equal("en-US", entry.Language);
        Assert.False(string.IsNullOrWhiteSpace(entry.Description));
        Assert.Equal("https://1337x.to/", entry.Links[0]);
        Assert.Contains("Movies/UHD", entry.Categories);
        Assert.Contains("TV/Anime", entry.Categories);
        Assert.Equal(entry.Categories.Count, entry.Categories.Distinct().Count());
        Assert.Equal(["q", "season", "ep"], entry.Definition.Caps.Modes["tv-search"]);
    }

    [Fact]
    public void DenyListDisablesAPublicIndexerWhateverTheCase()
    {
        IndexerCatalog catalog = Load(RealFixtures(), "1337X");

        Assert.False(catalog.Find("1337x")!.Enabled);
        Assert.Equal(3, catalog.Entries.Count);
    }

    [Fact]
    public void ReportsABrokenFileAndLoadsTheRest()
    {
        IndexerCatalog catalog = Load(new Dictionary<string, string>
        {
            ["1337x.yml"] = CatalogTestFiles.Fixture("1337x.yml"),
            ["broken.yml"] = "id: broken\nname: [unclosed",
            ["notes.txt"] = "not a definition",
        });

        Assert.Equal(["1337x"], catalog.Entries.Select(entry => entry.Id));
        DefinitionLoadError error = Assert.Single(catalog.Errors);
        Assert.Equal("broken.yml", error.File);
        Assert.Contains("Invalid indexer definition", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsADuplicateIdAndKeepsTheFirst()
    {
        IndexerCatalog catalog = Load(new Dictionary<string, string>
        {
            ["1337x.yml"] = CatalogTestFiles.Fixture("1337x.yml"),
            ["1337x-copy.yml"] = CatalogTestFiles.Fixture("1337x.yml"),
        });

        Assert.Single(catalog.Entries);
        Assert.Equal("1337x.yml", Assert.Single(catalog.Errors).File);
    }

    [Fact]
    public void MissingDirectoryIsAnEmptyCatalog()
    {
        IndexerCatalog catalog = IndexerCatalog.Load(new IndexerOptions { DefinitionsDirectory = _directory });

        Assert.Empty(catalog.Entries);
        Assert.Empty(catalog.Errors);
    }
}
