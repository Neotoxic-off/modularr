using System.Globalization;
using Modularr.Core.Qualities;

namespace Modularr.Core.Parsing.Episodes;

public sealed record ParsedEpisodeInfo
{
    public string ReleaseTitle { get; init; } = "";
    public string SeriesTitle { get; init; } = "";
    public SeriesTitleInfo SeriesTitleInfo { get; init; } = new("", "", 0, []);
    public QualityModel Quality { get; init; } = new(Qualities.Quality.Unknown, new Revision());
    public IReadOnlyList<int> SeasonNumbers { get; init; } = [];
    public IReadOnlyList<int> EpisodeNumbers { get; init; } = [];
    public IReadOnlyList<int> AbsoluteEpisodeNumbers { get; init; } = [];
    public IReadOnlyList<decimal> SpecialAbsoluteEpisodeNumbers { get; init; } = [];
    public DateOnly? AirDate { get; init; }
    public int? DailyPart { get; init; }
    public bool FullSeason { get; init; }
    public bool IsPartialSeason { get; init; }
    public int SeasonPart { get; init; }
    public bool IsSeasonExtra { get; init; }
    public bool IsSeasonTitle { get; init; }
    public bool IsSplitEpisode { get; init; }
    public bool IsMiniSeries { get; init; }
    public bool Special { get; init; }
    public string? ReleaseGroup { get; init; }
    public string? ReleaseHash { get; init; }
    public string ReleaseTokens { get; init; } = "";

    public bool IsMultiSeason => SeasonNumbers.Count > 1;

    private int FirstSeason => SeasonNumbers.Count > 0 ? SeasonNumbers[0] : 0;

    public bool IsDaily => AirDate is not null;

    public bool IsAbsoluteNumbering => AbsoluteEpisodeNumbers.Count > 0;

    public bool IsPossibleSpecialEpisode =>
        (AirDate is null && string.IsNullOrWhiteSpace(SeriesTitle) && (EpisodeNumbers.Count == 0 || SeasonNumbers is [0, ..]))
        || (!string.IsNullOrWhiteSpace(SeriesTitle) && Special)
        || EpisodeNumbers is [0];

    public bool IsPossibleSceneSeasonSpecial => SeasonNumbers is [not 0, ..] && EpisodeNumbers is [0];

    public ReleaseType ReleaseType =>
        EpisodeNumbers.Count > 1 || AbsoluteEpisodeNumbers.Count > 1 ? ReleaseType.MultiEpisode
        : EpisodeNumbers.Count == 1 || AbsoluteEpisodeNumbers.Count == 1 ? ReleaseType.SingleEpisode
        : FullSeason ? ReleaseType.SeasonPack
        : ReleaseType.Unknown;

    public override string ToString()
    {
        string episode = this switch
        {
            { IsDaily: true, EpisodeNumbers.Count: 0 } => AirDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            { FullSeason: true, IsMultiSeason: true } => string.Create(CultureInfo.InvariantCulture, $"Season {SeasonNumbers[0]:00}-{SeasonNumbers[^1]:00}"),
            { FullSeason: true } => SeasonNumbers.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $"Season {SeasonNumbers[0]:00}") : "[Unknown Season]",
            { EpisodeNumbers.Count: > 0 } => string.Create(CultureInfo.InvariantCulture, $"S{FirstSeason:00}E{string.Join("-", EpisodeNumbers.Select(e => e.ToString("00", CultureInfo.InvariantCulture)))}"),
            { AbsoluteEpisodeNumbers.Count: > 0 } => string.Join("-", AbsoluteEpisodeNumbers.Select(e => e.ToString("000", CultureInfo.InvariantCulture))),
            { Special: true } => FirstSeason != 0 ? string.Create(CultureInfo.InvariantCulture, $"[Unknown Season {SeasonNumbers[0]:00} Special]") : "[Unknown Special]",
            _ => "[Unknown Episode]",
        };

        return $"{SeriesTitle} - {episode} {Quality}";
    }
}
