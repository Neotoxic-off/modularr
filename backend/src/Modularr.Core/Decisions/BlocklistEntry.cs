namespace Modularr.Core.Decisions;

// A previously failed or rejected release of the same series or movie.
public sealed record BlocklistEntry(string Title, string? InfoHash = null, string? Indexer = null);
