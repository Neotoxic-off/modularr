using Modularr.Core.Qualities;

namespace Modularr.Core.Decisions;

// A file already in the library for a wanted episode or movie.
public sealed record ExistingFile(QualityModel Quality, string? ReleaseGroup = null);
