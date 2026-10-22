using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modularr.Core.Activities;
using Modularr.Core.Configuration;
using Modularr.Core.Discovery;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;

namespace Modularr.Core.Library;

public enum LibraryRoot
{
    Movies,
    Series,
    Anime,
}

public enum MatchConfidence
{
    None,
    Low,
    Medium,
    High,
}

// A library folder name as Sonarr, Radarr and Plex write them: "Title (Year)", "Title [tvdbid-123]", "Title {tmdb-123}", "{imdb-tt123}".
public sealed partial record LibraryFolderName(string Title, int? Year, string? IdSource, string? Id)
{
    // The add-box term (LookupQuery): the id when the name carries one, else the title and year.
    public string LookupTerm => Id is not null ? $"{IdSource}:{Id}" : Year is int year ? $"{Title} {year}" : Title;

    public static LibraryFolderName Parse(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Match id = IdTagRegex.Match(name);
        string title = IdTagRegex.Replace(name, " ");
        Match year = YearRegex.Match(title);
        title = (year.Success ? title[..year.Index] : title).Trim();
        return new LibraryFolderName(
            title,
            year.Success ? int.Parse(year.Groups["year"].Value, CultureInfo.InvariantCulture) : null,
            id.Success ? id.Groups["source"].Value.ToLowerInvariant() : null,
            id.Success ? id.Groups["id"].Value.ToLowerInvariant() : null);
    }

    [GeneratedRegex(@"[\[{](?<source>tmdb|tvdb|imdb)(?:id)?-(?<id>tt\d+|\d+)[\]}]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IdTagRegex { get; }

    [GeneratedRegex(@"\((?<year>(?:18|19|20)\d{2})\)", RegexOptions.CultureInvariant)]
    private static partial Regex YearRegex { get; }
}

// Folder is the full path; Match is the best metadata answer for its name, null when there is none.
public sealed record ImportProposal(string Folder, LibraryFolderName Parsed, DiscoveryItem? Match, MatchConfidence Confidence);

// Total counts every folder no title owns; Items is the first page of them.
public sealed record ImportProposals(int Total, IReadOnlyList<ImportProposal> Items);

public sealed record ImportItem(string Folder, int? TmdbId = null, int? TvdbId = null, int? QualityProfileId = null);

public sealed record ImportOutcome(string Folder, int? SeriesId, int? MovieId, int Files, string? Error);

// Upstream "Library Import": proposes a match for each folder of a root that no title owns, then adds the chosen ones with that
// folder as their path and records the files already in it. Nothing is searched, moved or renamed.
public sealed class LibraryImportService(
    ModularrDbContext db, DiscoveryService discovery, LibraryService library, DiskScanService scans, ActivityLog activities, IOptionsMonitor<ModularrOptions> options)
{
    // shortcut: a page of folders per request, spaced lookups; import a page and ask again. Batch lookups if libraries are huge.
    public const int PageSize = 50;
    private static readonly TimeSpan LookupSpacing = TimeSpan.FromMilliseconds(200);

    private static readonly EnumerationOptions TopLevel = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    public async Task<ImportProposals> ProposeAsync(LibraryRoot root, CancellationToken cancellationToken)
    {
        string rootPath = RootPath(root);
        List<string> owned = await OwnedFoldersAsync(cancellationToken).ConfigureAwait(false);
        string[] folders = Directory.Exists(rootPath)
            ? [.. Directory.EnumerateDirectories(rootPath, "*", TopLevel).Where(f => !owned.Exists(o => LibraryPaths.Overlaps(o, f))).Order(StringComparer.OrdinalIgnoreCase)]
            : [];

        List<ImportProposal> proposals = [];
        foreach (string folder in folders.Take(PageSize))
        {
            if (proposals.Count > 0)
            {
                await Task.Delay(LookupSpacing, cancellationToken).ConfigureAwait(false);
            }

            LibraryFolderName parsed = LibraryFolderName.Parse(Path.GetFileName(folder));
            IReadOnlyList<DiscoveryItem> found = await discovery.LookupAsync(
                parsed.LookupTerm, includeMovies: root == LibraryRoot.Movies, includeSeries: root != LibraryRoot.Movies, cancellationToken).ConfigureAwait(false);
            DiscoveryItem? match = found.FirstOrDefault(i => i.LibraryId is null);
            proposals.Add(new ImportProposal(folder, parsed, match, Confidence(parsed, match)));
        }

        return new ImportProposals(folders.Length, proposals);
    }

    // An id in the name is certain; otherwise the title must match, and the year too for High.
    public static MatchConfidence Confidence(LibraryFolderName parsed, DiscoveryItem? match)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return match switch
        {
            null => MatchConfidence.None,
            _ when parsed.Id is not null => MatchConfidence.High,
            _ when LookupQuery.Normalize(match.Title) != LookupQuery.Normalize(parsed.Title) => MatchConfidence.Low,
            _ when parsed.Year is not null && parsed.Year == match.Year => MatchConfidence.High,
            _ => MatchConfidence.Medium,
        };
    }

    // Each item stands alone: a rejected one reports its error and the others are still imported.
    public async Task<IReadOnlyList<ImportOutcome>> ImportAsync(IReadOnlyList<ImportItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        List<ImportOutcome> outcomes = [];
        foreach (ImportItem item in items)
        {
            try
            {
                outcomes.Add(await ImportAsync(item, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is ArgumentException or MetadataNotFoundException or MetadataSourceException or HttpRequestException
                or DbUpdateException or KeyNotFoundException)
            {
                db.ChangeTracker.Clear();
                outcomes.Add(new ImportOutcome(item?.Folder ?? "", null, null, 0, ex.Message));
            }
        }

        return outcomes;
    }

    private async Task<ImportOutcome> ImportAsync(ImportItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        string folder = await ValidFolderAsync(item, cancellationToken).ConfigureAwait(false);
        (string title, int? seriesId, int? movieId) = item.TmdbId is int tmdbId
            ? await AddMovieAsync(tmdbId, item.QualityProfileId, folder, cancellationToken).ConfigureAwait(false)
            : await AddSeriesAsync(item.TvdbId!.Value, item.QualityProfileId, folder, cancellationToken).ConfigureAwait(false);

        ScanResult scan = movieId is int id
            ? await scans.ScanMovieAsync(id, record: false, cancellationToken).ConfigureAwait(false)
            : await scans.ScanSeriesAsync(seriesId!.Value, record: false, cancellationToken).ConfigureAwait(false);
        await activities.RecordAsync(
            new Activity
            {
                Kind = ActivityKind.Imported,
                Level = scan.Added > 0 ? ActivityLevel.Success : ActivityLevel.Warning,
                Title = title,
                Message = scan.Added > 0 ? $"{scan.Added} existing file(s) imported from {folder}" : $"No video file recognized in {folder}",
                SeriesId = seriesId,
                MovieId = movieId,
            },
            cancellationToken).ConfigureAwait(false);
        return new ImportOutcome(folder, seriesId, movieId, scan.Added, null);
    }

    private async Task<(string, int?, int?)> AddMovieAsync(int tmdbId, int? qualityProfileId, string folder, CancellationToken cancellationToken)
    {
        if (await db.Movies.AnyAsync(m => m.TmdbId == tmdbId, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"Movie {tmdbId} is already in the library.");
        }

        Movie movie = await library.AddMovieAsync(tmdbId, qualityProfileId, version: null, search: false, cancellationToken).ConfigureAwait(false);
        movie.Path = folder;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (movie.Title, null, movie.Id);
    }

    private async Task<(string, int?, int?)> AddSeriesAsync(int tvdbId, int? qualityProfileId, string folder, CancellationToken cancellationToken)
    {
        if (await db.Series.AnyAsync(s => s.TvdbId == tvdbId, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException($"Series {tvdbId} is already in the library.");
        }

        Series series = await library.AddSeriesAsync(tvdbId, qualityProfileId, version: null, SeriesMonitor.All, seasons: null, search: false, cancellationToken).ConfigureAwait(false);
        series.Path = folder;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (series.Title, series.Id, null);
    }

    // A real folder directly inside the root for its kind, given as its full path (no "..", no link) and owned by no title.
    private async Task<string> ValidFolderAsync(ImportItem item, CancellationToken cancellationToken)
    {
        if ((item.TmdbId is null) == (item.TvdbId is null))
        {
            throw new ArgumentException("Give a tmdbId for a movie or a tvdbId for a series.");
        }

        if (string.IsNullOrWhiteSpace(item.Folder) || !Path.IsPathFullyQualified(item.Folder)
            || LibraryPaths.Normalize(item.Folder) != Path.TrimEndingDirectorySeparator(item.Folder))
        {
            throw new ArgumentException($"'{item.Folder}' is not a full folder path.");
        }

        string folder = LibraryPaths.Normalize(item.Folder);
        LibraryRoot[] roots = item.TmdbId is null ? [LibraryRoot.Series, LibraryRoot.Anime] : [LibraryRoot.Movies];
        if (!roots.Any(root => string.Equals(Path.GetDirectoryName(folder), LibraryPaths.Normalize(RootPath(root)), LibraryPaths.Comparison)))
        {
            throw new ArgumentException($"'{folder}' is not a folder of the {string.Join(" or ", roots).ToLowerInvariant()} root.");
        }

        DirectoryInfo directory = new(folder);
        if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            throw new ArgumentException($"'{folder}' does not exist or is a link.");
        }

        if ((await OwnedFoldersAsync(cancellationToken).ConfigureAwait(false)).Exists(owned => LibraryPaths.Overlaps(owned, folder)))
        {
            throw new ArgumentException($"'{folder}' already belongs to a title in the library.");
        }

        return folder;
    }

    private async Task<List<string>> OwnedFoldersAsync(CancellationToken cancellationToken) =>
        [
            .. await db.Series.Select(s => s.Path).ToListAsync(cancellationToken).ConfigureAwait(false),
            .. await db.Movies.Select(m => m.Path).ToListAsync(cancellationToken).ConfigureAwait(false),
        ];

    private string RootPath(LibraryRoot root)
    {
        PathOptions paths = options.CurrentValue.Paths;
        return root switch
        {
            LibraryRoot.Movies => paths.Movies,
            LibraryRoot.Anime => paths.Anime,
            _ => paths.Series,
        };
    }
}
