using Microsoft.Extensions.Options;
using Modularr.Core.Decisions;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Indexers;
using Modularr.Core.Library;
using Modularr.Core.Metadata;
using Modularr.Core.Persistence;
using Modularr.Core.PostProcessing;
using Modularr.Core.Subtitles;

namespace Modularr.Core.Configuration;

// Root of modularr.json, one section per module. Every default lives in the section records; the file only holds overrides.
// A class with setters, not a record: the options pattern creates it empty and fills it in place.
public sealed class ModularrOptions
{
    public const string FileName = "modularr.json";

    [ValidateObjectMembers]
    public PathOptions Paths { get; set; } = new();

    [ValidateObjectMembers]
    public TorrentEngineOptions Torrents { get; set; } = new();

    [ValidateObjectMembers]
    public PostProcessingOptions PostProcessing { get; set; } = new();

    [ValidateObjectMembers]
    public IndexerOptions Indexers { get; set; } = new();

    [ValidateObjectMembers]
    public DatabaseOptions Database { get; set; } = new();

    [ValidateObjectMembers]
    public MetadataOptions Metadata { get; set; } = new();

    [ValidateObjectMembers]
    public DecisionOptions Decisions { get; set; } = new();

    [ValidateObjectMembers]
    public MediaOptions Media { get; set; } = new();

    public SubtitleOptions Subtitles { get; set; } = new();

    // Every section, with the secrets a non-admin must not see removed.
    public ModularrOptions Redacted() => new()
    {
        Paths = Paths,
        Torrents = Torrents,
        PostProcessing = PostProcessing,
        Indexers = Indexers,
        Database = Database,
        Metadata = Metadata,
        Decisions = Decisions,
        Media = Media,
        Subtitles = Subtitles.Redacted(),
    };
}
