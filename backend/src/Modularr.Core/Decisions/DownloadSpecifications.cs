using System.Diagnostics;
using System.Text.RegularExpressions;
using Modularr.Core.Languages;
using Modularr.Core.PostProcessing;
using Modularr.Core.Qualities;

namespace Modularr.Core.Decisions;

// The torrent-relevant Sonarr/Radarr IDownloadDecisionEngineSpecification implementations, one method each.
// Each returns null when the release is acceptable.
public static partial class DownloadSpecifications
{
    private const int DefaultNewSeriesRuntime = 45;
    private const int DefaultMovieRuntime = 110;

    // Evaluation order of DownloadDecisionMaker; all of them run so a decision lists every rejection.
    public static IReadOnlyList<Func<ReleaseCandidate, DecisionContext, Rejection?>> All { get; } =
    [
        QualityAllowedByProfile,
        WantedLanguage,
        AcceptableSize,
        MaximumSize,
        MinimumAge,
        TorrentSeeding,
        NotSample,
        RawDisk,
        FullSeason,
        MultiSeason,
        SplitEpisode,
        Blocklist,
        Repack,
        AnimeVersionUpgrade,
        UpgradeAllowed,
        UpgradeDisk,
    ];

    [GeneratedRegex(@"(?:dis[ck])(?:[-_. ]\d+[-_. ])(?:(?:(?:480|720|1080|2160)[ip]|)[-_. ])?(?:Blu\-?ray)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiscRegex { get; }

    [GeneratedRegex(@"(?:(?:480|720|1080|2160)[ip]|)[-_. ](?:full)[-_. ](?:Blu\-?ray)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FullBlurayRegex { get; }

    [GeneratedRegex(@"(?:\d?x?M?DVD-?[R59])(?:[ ._]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DvdRegex { get; }

    public static Rejection? QualityAllowedByProfile(ReleaseCandidate candidate, DecisionContext context)
    {
        QualityProfileQualityItem qualityOrGroup = candidate.Profile.Items[candidate.Profile.GetIndex(candidate.Quality.Quality).Index];

        return qualityOrGroup.Allowed ? null : Reject(DownloadRejectionReason.QualityNotWanted, $"{candidate.Quality.Quality} is not wanted in profile");
    }

    // Radarr profile language (Sonarr v4 moved it to custom formats), the wanted language coming from the language policy.
    public static Rejection? WantedLanguage(ReleaseCandidate candidate, DecisionContext context)
    {
        Language wanted = context.WantedLanguage;

        if (wanted == Language.Any)
        {
            return null;
        }

        Language required = wanted == Language.Original ? candidate.OriginalLanguage : wanted;
        string found = string.Join(", ", candidate.Languages);

        // Like upstream's language aggregation, an untagged release is in the original language; so is the Original track of a
        // multi-audio release (MULTi, German DL).
        if (candidate.Languages.Any(l => (l == Language.Unknown || l == Language.Original ? candidate.OriginalLanguage : l) == required))
        {
            return null;
        }

        return wanted == Language.Original
            ? Reject(DownloadRejectionReason.WantedLanguage, $"Original Language ({required}) is wanted, but found {found}")
            : Reject(DownloadRejectionReason.WantedLanguage, $"{required} is wanted, but found {found}");
    }

    public static Rejection? AcceptableSize(ReleaseCandidate candidate, DecisionContext context)
    {
        long size = candidate.Release.Size ?? 0;

        if (candidate.Special || size == 0)
        {
            return null;
        }

        int runtime = candidate.MediaType == MediaType.Movie ? candidate.Runtime : EpisodesRuntime(candidate);

        // Radarr: a movie without runtime gets the median movie runtime; Sonarr: episodes without one are rejected.
        if (candidate.MediaType == MediaType.Movie && runtime == 0)
        {
            runtime = DefaultMovieRuntime;
        }

        if (runtime == 0)
        {
            return Reject(DownloadRejectionReason.UnknownRuntime, $"Runtime of all episodes is 0, unable to validate size until it is available");
        }

        QualityDefinition? definition = context.GetDefinition(candidate.MediaType, candidate.Quality.Quality);
        string runtimeMessage = candidate.MediaType == MediaType.Movie || candidate.Episodes.Count == 1
            ? $"{runtime}min"
            : $"{candidate.Episodes.Count} episodes totalling {runtime}min";

        if (definition?.MinSize is double minSize && size < Megabytes(minSize) * runtime)
        {
            return Reject(DownloadRejectionReason.BelowMinimumSize, $"{FormatSize(size)} is smaller than minimum allowed {FormatSize(Megabytes(minSize) * runtime)} (for {runtimeMessage})");
        }

        if (definition?.MaxSize is double maxSize and not 0 && size > Megabytes(maxSize) * runtime)
        {
            return Reject(DownloadRejectionReason.AboveMaximumSize, $"{FormatSize(size)} is larger than maximum allowed {FormatSize(Megabytes(maxSize) * runtime)} (for {runtimeMessage})");
        }

        return null;
    }

    public static Rejection? MaximumSize(ReleaseCandidate candidate, DecisionContext context)
    {
        long size = candidate.Release.Size ?? 0;
        long maximumSize = Megabytes(context.Options.MaximumSize);

        return maximumSize == 0 || size <= maximumSize
            ? null
            : Reject(DownloadRejectionReason.MaximumSizeExceeded, $"{FormatSize(size)} is too big, maximum size is {FormatSize(maximumSize)}");
    }

    // Sonarr/Radarr Usenet expected X: minimum age applies to Usenet only; modularr is torrent-only, so it applies to torrents.
    public static Rejection? MinimumAge(ReleaseCandidate candidate, DecisionContext context)
    {
        double age = (context.Now - candidate.Release.PublishDate).TotalMinutes;
        int minimumAge = context.Options.MinimumAge;

        return minimumAge == 0 || age >= minimumAge
            ? null
            : Reject(DownloadRejectionReason.MinimumAge, $"Only {Math.Round(age, 1)} minutes old, minimum age is {minimumAge} minutes", RejectionType.Temporary);
    }

    public static Rejection? TorrentSeeding(ReleaseCandidate candidate, DecisionContext context)
    {
        int minimumSeeders = context.Options.MinimumSeeders;

        return candidate.Release.Seeders is int seeders && seeders < minimumSeeders
            ? Reject(DownloadRejectionReason.MinimumSeeders, $"Not enough seeders: {seeders}. Minimum seeders: {minimumSeeders}")
            : null;
    }

    public static Rejection? NotSample(ReleaseCandidate candidate, DecisionContext context) =>
        candidate.Release.Title.Contains("sample", StringComparison.OrdinalIgnoreCase) && (candidate.Release.Size ?? 0) < SampleDetector.MaxSampleSize
            ? Reject(DownloadRejectionReason.Sample, $"Sample")
            : null;

    // shortcut: the Usenet container check (vob/iso/m2ts) is skipped, ReleaseInfo has no container; add one if a torrent indexer exposes it.
    public static Rejection? RawDisk(ReleaseCandidate candidate, DecisionContext context)
    {
        string title = candidate.Release.Title;

        return DiscRegex.IsMatch(title) || FullBlurayRegex.IsMatch(title) || DvdRegex.IsMatch(title)
            ? Reject(DownloadRejectionReason.Raw, $"Raw Bluray/DVD release")
            : null;
    }

    public static Rejection? FullSeason(ReleaseCandidate candidate, DecisionContext context) =>
        candidate.FullSeason
        && !context.Options.AllowSeasonPackWithoutAllEpisodesAired
        && candidate.Episodes.Any(e => e.AirDate is not DateTimeOffset airDate || airDate > context.Now.AddHours(24))
            ? Reject(DownloadRejectionReason.FullSeasonNotAired, $"Full season release rejected. All episodes haven't aired yet.")
            : null;

    public static Rejection? MultiSeason(ReleaseCandidate candidate, DecisionContext context) =>
        candidate.MultiSeason ? Reject(DownloadRejectionReason.MultiSeason, $"Multi-season releases are not supported") : null;

    public static Rejection? SplitEpisode(ReleaseCandidate candidate, DecisionContext context) =>
        candidate.SplitEpisode ? Reject(DownloadRejectionReason.SplitEpisode, $"Split episode releases are not supported") : null;

    // Upstream ReleaseComparer.SameTorrent over the blocklist rows matching the hash, else the title.
    public static Rejection? Blocklist(ReleaseCandidate candidate, DecisionContext context)
    {
        string? infoHash = candidate.Release.InfoHash;
        bool blocklisted = string.IsNullOrWhiteSpace(infoHash)
            ? context.Blocklist.Any(b =>
                b.Title.Contains(candidate.Release.Title, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(b.Indexer) || b.Indexer.Equals(candidate.Release.Indexer, StringComparison.OrdinalIgnoreCase)))
            : context.Blocklist.Any(b => infoHash.Equals(b.InfoHash, StringComparison.OrdinalIgnoreCase));

        return blocklisted ? Reject(DownloadRejectionReason.Blocklisted, $"Release is blocklisted") : null;
    }

    public static Rejection? Repack(ReleaseCandidate candidate, DecisionContext context)
    {
        ProperDownloadType propers = context.Options.DownloadPropersAndRepacks;

        if (!candidate.Quality.Revision.IsRepack || propers == ProperDownloadType.DoNotPrefer)
        {
            return null;
        }

        foreach (ExistingFile file in candidate.ExistingFiles.Where(f => UpgradableSpecification.IsRevisionUpgrade(f.Quality, candidate.Quality)))
        {
            if (propers == ProperDownloadType.DoNotUpgrade)
            {
                return Reject(DownloadRejectionReason.RepackDisabled, $"Repack downloading is disabled");
            }

            if (string.IsNullOrWhiteSpace(file.ReleaseGroup))
            {
                return Reject(DownloadRejectionReason.RepackUnknownReleaseGroup, $"Unable to determine release group for the existing file");
            }

            if (string.IsNullOrWhiteSpace(candidate.ReleaseGroup))
            {
                return Reject(DownloadRejectionReason.RepackUnknownReleaseGroup, $"Unable to determine release group for this release");
            }

            if (!file.ReleaseGroup.Equals(candidate.ReleaseGroup, StringComparison.OrdinalIgnoreCase))
            {
                return Reject(
                    DownloadRejectionReason.RepackReleaseGroupDoesNotMatch,
                    $"Release is a repack for a different release group. Release Group: {candidate.ReleaseGroup}. File release group: {file.ReleaseGroup}");
            }
        }

        return null;
    }

    // Anime v2/v3 releases replace the existing version only from the same group.
    public static Rejection? AnimeVersionUpgrade(ReleaseCandidate candidate, DecisionContext context)
    {
        if (candidate.MediaType != MediaType.Anime || context.Options.DownloadPropersAndRepacks == ProperDownloadType.DoNotPrefer)
        {
            return null;
        }

        foreach (ExistingFile file in candidate.ExistingFiles.Where(f => UpgradableSpecification.IsRevisionUpgrade(f.Quality, candidate.Quality)))
        {
            if (string.IsNullOrWhiteSpace(file.ReleaseGroup))
            {
                return Reject(DownloadRejectionReason.UnknownReleaseGroup, $"Existing release group is unknown");
            }

            if (string.IsNullOrWhiteSpace(candidate.ReleaseGroup))
            {
                return Reject(DownloadRejectionReason.UnknownReleaseGroup, $"Release group is unknown");
            }

            if (file.ReleaseGroup != candidate.ReleaseGroup)
            {
                return Reject(DownloadRejectionReason.ReleaseGroupDoesNotMatch, $"{candidate.ReleaseGroup} does not match existing release group {file.ReleaseGroup}");
            }
        }

        return null;
    }

    public static Rejection? UpgradeAllowed(ReleaseCandidate candidate, DecisionContext context) =>
        candidate.ExistingFiles.All(f => UpgradableSpecification.IsUpgradeAllowed(candidate.Profile, f.Quality, candidate.Quality))
            ? null
            : Reject(DownloadRejectionReason.QualityUpgradesDisabled, $"Existing file and the Quality profile does not allow upgrades");

    public static Rejection? UpgradeDisk(ReleaseCandidate candidate, DecisionContext context) =>
        candidate.MediaType != MediaType.Movie && candidate.FullSeason
            ? SeasonPackUpgrade(candidate, context)
            : candidate.ExistingFiles.Select(f => UpgradeDisk(candidate, f, context)).FirstOrDefault(r => r is not null);

    private static Rejection? SeasonPackUpgrade(ReleaseCandidate candidate, DecisionContext context)
    {
        int total = candidate.Episodes.Count;
        List<ExistingFile> existing = [.. candidate.ExistingFiles];

        if (total == 0 || existing.Count == 0)
        {
            return null;
        }

        // Missing episodes count as upgraded.
        int upgraded = total - existing.Count + existing.Count(f =>
            UpgradableSpecification.QualityCutoffNotMet(candidate.Profile, f.Quality, candidate.Quality)
            && UpgradableSpecification.IsUpgradable(candidate.Profile, f.Quality, candidate.Quality, context.Options.DownloadPropersAndRepacks) == UpgradeableRejectReason.None);

        SeasonPackUpgradeType mode = context.Options.SeasonPackUpgrade;
        double percentage = (double)upgraded / total * 100;
        bool accepted = mode switch
        {
            SeasonPackUpgradeType.Any => upgraded > 0,
            SeasonPackUpgradeType.All => percentage >= 100.0,
            _ => percentage >= context.Options.SeasonPackUpgradeThreshold,
        };

        return accepted
            ? null
            : Reject(
                DownloadRejectionReason.DiskNotUpgrade,
                $"Season pack does not meet the upgrade criteria. Upgradable: {upgraded}/{total} ({percentage:0.##}%), Mode: {mode}, Threshold: {context.Options.SeasonPackUpgradeThreshold}%");
    }

    private static Rejection? UpgradeDisk(ReleaseCandidate candidate, ExistingFile file, DecisionContext context)
    {
        QualityProfile profile = candidate.Profile;

        if (!UpgradableSpecification.QualityCutoffNotMet(profile, file.Quality, candidate.Quality))
        {
            int cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirstAllowedQuality().Id;
            return Reject(DownloadRejectionReason.DiskCutoffMet, $"Existing file meets cutoff: {profile.Items[profile.GetIndex(cutoff).Index]}");
        }

        return UpgradableSpecification.IsUpgradable(profile, file.Quality, candidate.Quality, context.Options.DownloadPropersAndRepacks) switch
        {
            UpgradeableRejectReason.None => null,
            UpgradeableRejectReason.BetterQuality => Reject(DownloadRejectionReason.DiskHigherPreference, $"Existing file on disk is of equal or higher preference: {file.Quality}"),
            UpgradeableRejectReason.BetterRevision => Reject(DownloadRejectionReason.DiskHigherRevision, $"Existing file on disk is of equal or higher revision: {file.Quality.Revision}"),
            UpgradeableRejectReason.QualityCutoff => Reject(DownloadRejectionReason.DiskCutoffMet, $"Existing file on disk meets quality cutoff: {profile.Items[profile.GetIndex(profile.Cutoff).Index]}"),
            UpgradeableRejectReason.CustomFormatScore => Reject(DownloadRejectionReason.DiskCustomFormatScore, $"Existing file on disk has a equal or higher Custom Format score: 0"),
            UpgradeableRejectReason.UpgradesNotAllowed => Reject(DownloadRejectionReason.DiskUpgradesNotAllowed, $"Existing file on disk and Quality Profile '{profile.Name}' does not allow upgrades"),
            _ => throw new UnreachableException(),
        };
    }

    // Each episode's runtime, or the series runtime; a new series without runtime gets 45 minutes for its first day of episodes.
    private static int EpisodesRuntime(ReleaseCandidate candidate)
    {
        int seriesRuntime = candidate.Runtime;

        if (seriesRuntime == 0
            && candidate.Pilot is EpisodeTarget pilot
            && candidate.Episodes.Count > 0
            && candidate.Episodes[0].SeasonNumber == pilot.SeasonNumber)
        {
            // Without an air date the publish date stands in: like the runtime, it may not be known yet.
            DateTimeOffset gracePeriodEnd = (pilot.AirDate ?? candidate.Release.PublishDate).AddHours(24);

            if (candidate.Episodes.All(e => e.AirDate is not DateTimeOffset airDate || airDate < gracePeriodEnd))
            {
                seriesRuntime = DefaultNewSeriesRuntime;
            }
        }

        return candidate.Episodes.Sum(e => e.Runtime > 0 ? e.Runtime : seriesRuntime);
    }

    internal static long Megabytes(double megabytes) => Convert.ToInt64(megabytes * 1024 * 1024);

    private static string FormatSize(long bytes) => FormattableString.Invariant($"{bytes / 1024.0 / 1024.0:0.#} MB");

    private static Rejection Reject(DownloadRejectionReason reason, FormattableString message, RejectionType type = RejectionType.Permanent) =>
        new(reason, FormattableString.Invariant(message), type);
}
