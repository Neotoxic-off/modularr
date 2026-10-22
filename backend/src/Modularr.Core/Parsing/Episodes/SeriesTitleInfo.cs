namespace Modularr.Core.Parsing.Episodes;

public sealed record SeriesTitleInfo(string Title, string TitleWithoutYear, int Year, IReadOnlyList<string> AllTitles);
