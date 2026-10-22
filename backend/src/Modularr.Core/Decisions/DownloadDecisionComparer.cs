namespace Modularr.Core.Decisions;

// Upstream DownloadDecisionComparer: greater is better. Releases must belong to the same series or movie.
// Torrent-only: the preferred protocol and Usenet age steps never apply; custom format score and Radarr indexer flags are not ported.
public sealed class DownloadDecisionComparer(DecisionContext context) : IComparer<DownloadDecision>
{
    private static readonly long SizeRounding = DownloadSpecifications.Megabytes(200);

    public int Compare(DownloadDecision? x, DownloadDecision? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        ReleaseCandidate left = x.Candidate;
        ReleaseCandidate right = y.Candidate;
        int[] steps =
        [
            CompareQuality(left, right),
            CompareEpisodeCount(left, right),
            CompareEpisodeNumber(left, right),
            right.Release.IndexerPriority.CompareTo(left.Release.IndexerPriority),
            ComparePeers(left, right),
            SizeScore(left).CompareTo(SizeScore(right)),
        ];

        return steps.FirstOrDefault(result => result != 0);
    }

    private int CompareQuality(ReleaseCandidate left, ReleaseCandidate right)
    {
        int quality = left.Profile.GetIndex(left.Quality.Quality).CompareTo(right.Profile.GetIndex(right.Quality.Quality), respectGroupOrder: true);

        return quality != 0 || context.Options.DownloadPropersAndRepacks == ProperDownloadType.DoNotPrefer
            ? quality
            : left.Quality.Revision.CompareTo(right.Quality.Revision);
    }

    // Season packs first, then anime prefers more episodes per release and everything else fewer.
    private static int CompareEpisodeCount(ReleaseCandidate left, ReleaseCandidate right)
    {
        int seasonPack = left.FullSeason.CompareTo(right.FullSeason);

        if (seasonPack != 0)
        {
            return seasonPack;
        }

        return left.MediaType == MediaType.Anime && right.MediaType == MediaType.Anime
            ? left.Episodes.Count.CompareTo(right.Episodes.Count)
            : right.Episodes.Count.CompareTo(left.Episodes.Count);
    }

    private static int CompareEpisodeNumber(ReleaseCandidate left, ReleaseCandidate right) =>
        FirstEpisodeNumber(right).CompareTo(FirstEpisodeNumber(left));

    private static int FirstEpisodeNumber(ReleaseCandidate candidate) =>
        candidate.Episodes.Count == 0 ? 0 : candidate.Episodes.Min(e => e.EpisodeNumber);

    // Orders of magnitude only, so 1000 and 1100 seeders tie and the next step decides.
    private static int ComparePeers(ReleaseCandidate left, ReleaseCandidate right)
    {
        int seeders = Magnitude(left.Release.Seeders).CompareTo(Magnitude(right.Release.Seeders));

        return seeders != 0 ? seeders : Magnitude(left.Release.Peers).CompareTo(Magnitude(right.Release.Peers));
    }

    private static double Magnitude(int? count) => count is int value and > 0 ? Math.Round(Math.Log10(value)) : 0;

    // Closest to the preferred size wins; without a preferred size (or runtime) the largest wins. Both rounded to 200 MB.
    private long SizeScore(ReleaseCandidate candidate)
    {
        long size = candidate.Release.Size ?? 0;
        double? preferredSize = context.GetDefinition(candidate.MediaType, candidate.Quality.Quality)?.PreferredSize;
        int runtime = candidate.MediaType == MediaType.Movie || candidate.Episodes.Count == 0
            ? candidate.Runtime
            : candidate.Episodes.Sum(e => e.Runtime > 0 ? e.Runtime : candidate.Runtime);

        return preferredSize is double preferred && runtime > 0
            ? -Math.Abs((size - (runtime * DownloadSpecifications.Megabytes(preferred))) / SizeRounding * SizeRounding)
            : size / SizeRounding * SizeRounding;
    }
}
