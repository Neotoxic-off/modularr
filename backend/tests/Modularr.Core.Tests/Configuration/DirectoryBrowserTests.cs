using Modularr.Core.Configuration;

namespace Modularr.Core.Tests.Configuration;

public sealed class DirectoryBrowserTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("modularr-browse-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ListsSubdirectoriesSortedByNameWithoutFiles()
    {
        foreach (string name in new[] { "b", "A", "c" })
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
        }

        File.WriteAllText(Path.Combine(_root, "file.txt"), "");

        DirectoryListing listing = DirectoryBrowser.Browse(_root);

        Assert.Equal(_root, listing.Path);
        Assert.Equal(Directory.GetParent(_root)!.FullName, listing.Parent);
        Assert.Equal(["A", "b", "c"], listing.Directories.Select(d => d.Name));
        Assert.Equal(Path.Combine(_root, "A"), listing.Directories[0].Path);
    }

    [Fact]
    public void WithoutAPathListsTheRoots()
    {
        DirectoryListing listing = DirectoryBrowser.Browse(null);

        Assert.Null(listing.Path);
        Assert.Null(listing.Parent);
        Assert.NotEmpty(listing.Directories);
        Assert.All(listing.Directories, d => Assert.Null(Path.GetDirectoryName(d.Path)));
    }

    [Fact]
    public void RootHasNoParent()
    {
        Assert.Null(DirectoryBrowser.Browse(Path.GetPathRoot(_root)).Parent);
    }

    [Fact]
    public void RejectsRelativeAndMissingPaths()
    {
        Assert.Throws<ArgumentException>(() => DirectoryBrowser.Browse("relative/dir"));
        Assert.Throws<ArgumentException>(() => DirectoryBrowser.Browse(Path.Combine(_root, "missing")));
    }
}
