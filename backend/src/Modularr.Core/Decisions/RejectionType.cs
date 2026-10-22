namespace Modularr.Core.Decisions;

public enum RejectionType
{
    // The release will never be accepted as is.
    Permanent,

    // The release may be accepted later (e.g. once it is old enough).
    Temporary,
}
