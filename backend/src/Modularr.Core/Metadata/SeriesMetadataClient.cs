using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;

namespace Modularr.Core.Metadata;

// Port of Sonarr's SkyHookProxy: TVDB-backed series search and lookup through skyhook.sonarr.tv.
public sealed partial class SeriesMetadataClient(HttpClient httpClient, IOptions<ModularrOptions> options)
{
    public const string OfficialSeasonType = "official";

    private readonly MetadataOptions _options = options.Value.Metadata;

    // "tvdb:123" / "tvdbid:123" resolve locally; every other term (incl. "imdb:tt..", "tmdb:..") goes to SkyHook's search.
    public async Task<IReadOnlyList<SeriesInfo>> SearchSeriesAsync(string term, Language? language = null, CancellationToken cancellationToken = default)
    {
        if (RootedPathRegex.IsMatch(term))
        {
            throw new InvalidSearchTermException(term);
        }

        string lowerTerm = term.ToLowerInvariant();

        if (lowerTerm.StartsWith("tvdb:", StringComparison.Ordinal) || lowerTerm.StartsWith("tvdbid:", StringComparison.Ordinal))
        {
            string slug = lowerTerm.Split(':')[1].Trim();

            if (!int.TryParse(slug, NumberStyles.None, CultureInfo.InvariantCulture, out int tvdbId) || tvdbId <= 0)
            {
                return [];
            }

            try
            {
                SeriesInfo series = await GetSeriesAsync(tvdbId, language, OfficialSeasonType, cancellationToken).ConfigureAwait(false);
                return [series with { Episodes = [] }];
            }
            catch (MetadataNotFoundException)
            {
                return [];
            }
        }

        string url = MetadataHttp.Combine(_options.SkyHookUrl,
            $"search/?language={LanguageCode(language)}&term={Uri.EscapeDataString(lowerTerm.Trim())}");
        List<ShowResource> shows = await MetadataHttp.GetJsonAsync<List<ShowResource>>(httpClient, url, cancellationToken).ConfigureAwait(false)
            ?? throw new MetadataSourceException($"Search for '{term}' failed: SkyHook answered 404.");
        IReadOnlySet<int> dailySeries = await GetDailySeriesIdsAsync(cancellationToken).ConfigureAwait(false);

        return [.. shows.Select(show => MapSeries(show, dailySeries))];
    }

    // seasonType is one of the show's SeasonTypes (official, dvd, absolute, ...); it picks the episode numbering.
    public async Task<SeriesInfo> GetSeriesAsync(int tvdbId, Language? language = null, string seasonType = OfficialSeasonType, CancellationToken cancellationToken = default)
    {
        string url = MetadataHttp.Combine(_options.SkyHookUrl,
            $"shows/{LanguageCode(language)}/{tvdbId.ToString(CultureInfo.InvariantCulture)}/{Uri.EscapeDataString(seasonType)}");
        ShowResource show = await MetadataHttp.GetJsonAsync<ShowResource>(httpClient, url, cancellationToken).ConfigureAwait(false)
            ?? throw new MetadataNotFoundException(tvdbId.ToString(CultureInfo.InvariantCulture));
        IReadOnlySet<int> dailySeries = await GetDailySeriesIdsAsync(cancellationToken).ConfigureAwait(false);

        return MapSeries(show, dailySeries) with { Episodes = [.. show.Episodes.Select(MapEpisode)] };
    }

    // shortcut: fetched on every lookup (upstream caches it for an hour), cache it if lookups become frequent.
    private async Task<IReadOnlySet<int>> GetDailySeriesIdsAsync(CancellationToken cancellationToken)
    {
        try
        {
            List<DailySeriesResource>? daily = await MetadataHttp.GetJsonAsync<List<DailySeriesResource>>(httpClient,
                MetadataHttp.Combine(_options.SonarrServicesUrl, "dailyseries"), cancellationToken).ConfigureAwait(false);
            return daily?.Select(d => d.TvdbId).ToHashSet() ?? [];
        }
        catch (MetadataSourceException)
        {
            // Upstream treats an unreachable list as "no daily shows" rather than failing the lookup.
            return new HashSet<int>();
        }
    }

    private static string LanguageCode(Language? language) =>
        (IsoLanguages.Get(language ?? Language.English) ?? IsoLanguages.Get(Language.English)!).ThreeLetterCode;

    private static SeriesInfo MapSeries(ShowResource show, IReadOnlySet<int> dailySeries)
    {
        string? originalTitle = show.Translations.FirstOrDefault(t => t.Language == show.OriginalLanguage)?.Title;

        return new SeriesInfo
        {
            TvdbId = show.TvdbId,
            TvRageId = show.TvRageId,
            TvMazeId = show.TvMazeId,
            TmdbId = show.TmdbId,
            ImdbId = show.ImdbId,
            MalIds = show.MalIds ?? [],
            AniListIds = show.AniListIds ?? [],
            Title = show.Title,
            OriginalTitle = originalTitle,
            OriginalLanguage = string.IsNullOrWhiteSpace(show.OriginalLanguage)
                ? Language.English
                : IsoLanguages.Find(show.OriginalLanguage)?.Language ?? Language.English,
            OriginalCountry = show.OriginalCountry,
            Overview = show.Overview,
            FirstAired = show.FirstAired,
            LastAired = show.LastAired,
            Runtime = show.Runtime ?? 0,
            Network = show.Network,
            AirTime = show.TimeOfDay is { } time ? new TimeOnly(time.Hours, time.Minutes) : null,
            TitleSlug = show.Slug,
            Status = show.Status?.ToLowerInvariant() switch
            {
                "ended" => SeriesStatus.Ended,
                "upcoming" => SeriesStatus.Upcoming,
                _ => SeriesStatus.Continuing,
            },
            Ratings = MapRating(show.Rating),
            // A JSON null overrides the [] initializer; Genres is a required column.
            Genres = show.Genres ?? [],
            Certification = string.IsNullOrWhiteSpace(show.ContentRating) ? null : show.ContentRating.ToUpperInvariant(),
            IsDaily = dailySeries.Contains(show.TvdbId),
            Actors = [.. show.Actors.Select(a => new SeriesActor(a.Name, a.Character,
                a.Image is null ? [] : [new MetadataImage(MediaCoverType.Headshot, a.Image)]))],
            Seasons = [.. show.Seasons.DistinctBy(s => s.SeasonNumber)
                .Select(s => new SeasonInfo(s.SeasonNumber, s.Title, [.. (s.Images ?? []).Select(i => MetadataImage.From(i.CoverType, i.Url))]))],
            Images = [.. show.Images.Select(i => MetadataImage.From(i.CoverType, i.Url))],
            SeasonTypes = [.. show.SeasonTypes.Select(t => new SeasonTypeInfo(t.Name, t.Type, t.SeasonNumbers ?? [], t.EpisodeCount))],
            Translations = [.. show.Translations
                .Select(t => (Iso: t.Language is null ? null : IsoLanguages.Find(t.Language), Translation: t))
                .Where(t => t.Iso is not null && !string.IsNullOrWhiteSpace(t.Translation.Title))
                .Select(t => new SeriesTranslation(t.Iso!.Language, t.Translation.Title!, t.Translation.Overview))],
        };
    }

    private static EpisodeInfo MapEpisode(EpisodeResource episode) => new()
    {
        TvdbId = episode.TvdbId,
        SeasonNumber = episode.SeasonNumber,
        EpisodeNumber = episode.EpisodeNumber,
        AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber,
        AiredAfterSeasonNumber = episode.AiredAfterSeasonNumber,
        AiredBeforeSeasonNumber = episode.AiredBeforeSeasonNumber,
        AiredBeforeEpisodeNumber = episode.AiredBeforeEpisodeNumber,
        Title = episode.Title,
        Overview = episode.Overview,
        AirDate = episode.AirDate,
        AirDateUtc = episode.AirDateUtc,
        Runtime = episode.Runtime,
        FinaleType = episode.FinaleType,
        Ratings = MapRating(episode.Rating),
        // The episode's own still only; series fanart is not a screenshot.
        Images = episode.Image is null ? [] : [new MetadataImage(MediaCoverType.Screenshot, episode.Image)],
    };

    private static MetadataRating MapRating(RatingResource? rating) => rating is null ? default : new(rating.Count, rating.Value);

    // Sonarr rejects terms that are rooted paths (Windows drive/UNC or Unix), whatever the host OS.
    [GeneratedRegex(@"^(?:[\\/]|[a-zA-Z]:\\)")]
    private static partial Regex RootedPathRegex { get; }

    private sealed record ShowResource
    {
        public int TvdbId { get; init; }
        public required string Title { get; init; }
        public string? Overview { get; init; }
        public string? Slug { get; init; }
        public DateOnly? FirstAired { get; init; }
        public DateOnly? LastAired { get; init; }
        public int? TvRageId { get; init; }
        public int? TvMazeId { get; init; }
        public int? TmdbId { get; init; }
        public List<int>? MalIds { get; init; }
        public List<int>? AniListIds { get; init; }
        public string? Status { get; init; }
        public int? Runtime { get; init; }
        public TimeOfDayResource? TimeOfDay { get; init; }
        public string? Network { get; init; }
        public string? ImdbId { get; init; }
        public string? OriginalLanguage { get; init; }
        public string? OriginalCountry { get; init; }
        public List<ActorResource> Actors { get; init; } = [];
        public List<string> Genres { get; init; } = [];
        public string? ContentRating { get; init; }
        public RatingResource? Rating { get; init; }
        public List<ImageResource> Images { get; init; } = [];
        public List<SeasonResource> Seasons { get; init; } = [];
        public List<EpisodeResource> Episodes { get; init; } = [];
        public List<TranslationResource> Translations { get; init; } = [];
        public List<SeasonTypeResource> SeasonTypes { get; init; } = [];
    }

    private sealed record EpisodeResource
    {
        public int TvdbId { get; init; }
        public int SeasonNumber { get; init; }
        public int EpisodeNumber { get; init; }
        public int? AbsoluteEpisodeNumber { get; init; }
        public int? AiredAfterSeasonNumber { get; init; }
        public int? AiredBeforeSeasonNumber { get; init; }
        public int? AiredBeforeEpisodeNumber { get; init; }
        public string? Title { get; init; }
        public DateOnly? AirDate { get; init; }
        public DateTime? AirDateUtc { get; init; }
        public int Runtime { get; init; }
        public string? FinaleType { get; init; }
        public RatingResource? Rating { get; init; }
        public string? Overview { get; init; }
        public string? Image { get; init; }
    }

    private sealed record ActorResource(string Name, string? Character, string? Image);

    private sealed record ImageResource(string? CoverType, string Url);

    private sealed record RatingResource(int Count, decimal Value);

    private sealed record SeasonResource(int SeasonNumber, string? Title, List<ImageResource>? Images);

    private sealed record SeasonTypeResource(string Name, string Type, List<int>? SeasonNumbers, int EpisodeCount);

    private sealed record TimeOfDayResource(int Hours, int Minutes);

    private sealed record TranslationResource(string? Language, string? Title, string? Overview);

    private sealed record DailySeriesResource(int TvdbId);
}
