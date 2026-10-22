using Microsoft.Extensions.Options;
using Modularr.Core.Downloads.Torrents;
using Modularr.Core.Indexers;
using Modularr.Core.PostProcessing;

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
}
