namespace Modularr.Core.Languages;

public sealed record SubtitleTitleInfo(Language Language, IReadOnlyList<string> LanguageTags)
{
    public string? RawTitle { get; init; }
    public string? Title { get; init; }
    public int Copy { get; init; }
    public bool TitleFirst { get; init; }
}
