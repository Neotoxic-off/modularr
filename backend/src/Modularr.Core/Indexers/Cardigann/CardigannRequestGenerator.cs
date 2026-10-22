using System.Globalization;
using System.Net;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Indexers.Cardigann;

// One HTTP request of a search: a search path with its inputs, plus the template variables the parser continues with.
public sealed record CardigannRequest(
    Uri Url,
    HttpMethod Method,
    IReadOnlyList<KeyValuePair<string, string>> Form,
    IReadOnlyDictionary<string, string> Headers,
    SearchPathBlock SearchPath,
    Dictionary<string, object?> Variables);

// Prowlarr CardigannRequestGenerator.GetRequest: one request per search path whose categories match.
internal static class CardigannRequestGenerator
{
    private static readonly string[] KeywordVariables = [".Query.Q", ".Query.Series", ".Query.Movie", ".Query.Year", ".Query.Episode"];

    public static IReadOnlyList<CardigannRequest> Build(CardigannSite site, CardigannSearchQuery query)
    {
        SearchBlock search = site.Definition.Search;
        Dictionary<string, object?> variables = QueryVariables(site, query);

        IReadOnlyList<string> mappedCategories = site.Categories.MapQueryCategories(query.Categories);
        if (mappedCategories.Count == 0)
        {
            mappedCategories = site.Categories.DefaultTrackerCategories;
        }

        variables[".Query.Keywords"] = string.Join(" ", KeywordVariables.Select(name => variables[name] as string).Where(value => !string.IsNullOrWhiteSpace(value)));
        variables[".Keywords"] = CardigannFilters.Apply((string)variables[".Query.Keywords"]!, search.KeywordsFilters, variables, site.Encoding);

        List<CardigannRequest> requests = [];
        foreach (SearchPathBlock searchPath in search.Paths)
        {
            variables[".Categories"] = mappedCategories;

            // A path limited to categories runs only when the search asks for one of them ("!" first inverts the list).
            if (searchPath.Categories is { Count: > 0 } pathCategories && mappedCategories.Count > 0)
            {
                if (mappedCategories.Intersect(pathCategories).Any() == (pathCategories[0] == "!"))
                {
                    continue;
                }

                variables[".Categories"] = mappedCategories.Intersect(pathCategories).ToList();
            }

            // UrlEncode turns spaces into '+', which a path segment would keep literally.
            string url = site.ResolvePath(CardigannTemplate.Apply(searchPath.Path, variables, WebUtility.UrlEncode)?.Replace("+", "%20", StringComparison.Ordinal)).AbsoluteUri;
            HttpMethod method = string.Equals(searchPath.Method, "post", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Post : HttpMethod.Get;

            List<KeyValuePair<string, string>> inputs = [];
            IEnumerable<IReadOnlyDictionary<string, string>?> inputBlocks = searchPath.InheritInputs ? [search.Inputs, searchPath.Inputs] : [searchPath.Inputs];
            foreach ((string key, string value) in inputBlocks.SelectMany(block => block ?? new Dictionary<string, string>()))
            {
                if (key == "$raw")
                {
                    // Pre-built "a=1&b=2" pairs (upstream encodes their values once more when building the query).
                    foreach (string[] pair in (CardigannTemplate.Apply(value, variables, WebUtility.UrlEncode) ?? "").Split('&').Select(part => part.Split('=', 2)).Where(pair => pair[0].Length > 0))
                    {
                        inputs.Add(new(pair[0], pair.Length == 2 ? pair[1] : ""));
                    }

                    continue;
                }

                string input = CardigannTemplate.Apply(value, variables) ?? "";
                if (!string.IsNullOrWhiteSpace(input) || search.AllowEmptyInputs)
                {
                    inputs.Add(new(key, input));
                }
            }

            if (method == HttpMethod.Get && inputs.Count > 0)
            {
                url += (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + string.Join("&", inputs.Select(input => $"{input.Key}={CardigannFilters.UrlEncode(input.Value, site.Encoding)}"));
            }

            // Two paths can render the same GET request.
            if (method == HttpMethod.Get && requests.Any(request => request.Method == HttpMethod.Get && request.Url.AbsoluteUri == new Uri(url).AbsoluteUri))
            {
                continue;
            }

            requests.Add(new CardigannRequest(
                new Uri(url),
                method,
                method == HttpMethod.Post ? inputs : [],
                CardigannSite.Headers(search.Headers, variables),
                searchPath,
                new Dictionary<string, object?>(variables, StringComparer.Ordinal)));
        }

        return requests;
    }

    private static Dictionary<string, object?> QueryVariables(CardigannSite site, CardigannSearchQuery query)
    {
        Dictionary<string, object?> variables = site.BaseVariables();
        bool isTv = query.Type == CardigannSearchType.TvSearch;
        bool isMovie = query.Type == CardigannSearchType.MovieSearch;

        variables[".Query.Type"] = query.TypeName;
        variables[".Query.Q"] = query.Term;
        variables[".Query.Categories"] = query.Categories.Select(category => category.ToString(CultureInfo.InvariantCulture)).ToList();
        variables[".Query.Limit"] = null;
        variables[".Query.Offset"] = null;
        variables[".Query.Extended"] = null;
        variables[".Query.APIKey"] = null;
        variables[".Query.Genre"] = null;
        variables[".Query.Movie"] = null;
        variables[".Query.Year"] = isTv || isMovie ? query.Year?.ToString(CultureInfo.InvariantCulture) : null;
        variables[".Query.IMDBID"] = isTv || isMovie ? query.FullImdbId : null;
        variables[".Query.IMDBIDShort"] = isTv || isMovie ? query.ImdbId?.ToString("D7", CultureInfo.InvariantCulture) : null;
        variables[".Query.TMDBID"] = isTv || isMovie ? query.TmdbId?.ToString(CultureInfo.InvariantCulture) : null;
        variables[".Query.Series"] = null;
        variables[".Query.Ep"] = isTv ? query.Episode : null;
        variables[".Query.Season"] = isTv ? query.Season?.ToString(CultureInfo.InvariantCulture) : null;
        variables[".Query.TVDBID"] = isTv ? query.TvdbId?.ToString(CultureInfo.InvariantCulture) : null;
        variables[".Query.TVRageID"] = null;
        variables[".Query.TVMazeID"] = null;
        variables[".Query.TraktID"] = null;
        variables[".Query.DoubanID"] = null;
        variables[".Query.Episode"] = isTv ? query.EpisodeSearchString : null;
        variables[".Query.Album"] = null;
        variables[".Query.Artist"] = null;
        variables[".Query.Label"] = null;
        variables[".Query.Track"] = null;
        variables[".Query.Author"] = null;
        variables[".Query.Title"] = null;
        variables[".Query.Publisher"] = null;
        return variables;
    }
}
