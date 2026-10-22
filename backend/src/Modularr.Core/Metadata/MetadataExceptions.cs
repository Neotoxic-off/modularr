namespace Modularr.Core.Metadata;

// The metadata source answered 404 (or nothing usable) for this id.
public sealed class MetadataNotFoundException(string id) : Exception($"Metadata source has no entry for '{id}'.")
{
    public string Id { get; } = id;
}

// The metadata source could not be reached or answered something unreadable (upstream SkyHookException).
public sealed class MetadataSourceException(string message, Exception? innerException = null) : Exception(message, innerException);

// A lookup term that is a filesystem path, not a title (upstream InvalidSearchTermException).
public sealed class InvalidSearchTermException(string term) : Exception($"Invalid search term '{term}'.")
{
    public string Term { get; } = term;
}
