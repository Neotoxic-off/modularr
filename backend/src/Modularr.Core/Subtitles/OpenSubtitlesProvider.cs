using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Languages;
using Modularr.Core.Parsing;

namespace Modularr.Core.Subtitles;

// OpenSubtitles.com REST API v1 (https://opensubtitles.stoplight.io). Searches by IMDb or TMDB id for movies, by series title,
// season and episode for episodes (the API takes no TVDB id), plus the moviehash. Downloads go through /download, which
// answers with a link on an opensubtitles.com host.
// shortcut: VIP accounts get their own api host at login, it is ignored and api.opensubtitles.com is always used.
public sealed class OpenSubtitlesProvider(HttpClient httpClient, IOptionsMonitor<ModularrOptions> options, IMemoryCache cache) : ISubtitleProvider
{
    public const string ProviderName = "OpenSubtitles";

    private const string Api = "https://api.opensubtitles.com/api/v1/";
    private const int MaxRedirects = 3;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string Name => ProviderName;

    public bool Enabled => Account.Enabled;

    private OpenSubtitlesOptions Account => options.CurrentValue.Subtitles.OpenSubtitles;

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Dictionary<string, string> parameters = new() { ["languages"] = string.Join(',', query.Languages.Select(Code).OfType<string>().Order(StringComparer.Ordinal)) };
        if (query.IsEpisode)
        {
            parameters["query"] = query.Title;
            parameters["season_number"] = query.Season!.Value.ToString(CultureInfo.InvariantCulture);
            parameters["episode_number"] = query.Episode!.Value.ToString(CultureInfo.InvariantCulture);
            parameters["type"] = "episode";
        }
        else if (ImdbNumber(query.ImdbId) is string imdb)
        {
            parameters["imdb_id"] = imdb;
        }
        else if (query.TmdbId is int tmdb)
        {
            parameters["tmdb_id"] = tmdb.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            parameters["query"] = query.Title;
        }

        if (query.MovieHash is not null)
        {
            parameters["moviehash"] = query.MovieHash;
        }

        // The API wants its parameters sorted and its lists comma separated, and answers a redirect otherwise.
        string url = Api + "subtitles?" + string.Join('&', parameters.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value).Replace("%2C", ",", StringComparison.Ordinal)}"));
        SearchResponse response = await SendJsonAsync<SearchResponse>(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);
        return Map(response, query);
    }

    public async Task<SubtitleDownload> DownloadAsync(string id, CancellationToken cancellationToken)
    {
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long fileId))
        {
            throw new ArgumentException($"'{id}' is not an OpenSubtitles file id.", nameof(id));
        }

        DownloadResponse link = await SendJsonAsync<DownloadResponse>(HttpMethod.Post, Api + "download", new { file_id = fileId }, cancellationToken).ConfigureAwait(false);
        if (link.Link is null || !Uri.TryCreate(link.Link, UriKind.Absolute, out Uri? uri))
        {
            throw new HttpRequestException("OpenSubtitles gave no download link (daily quota reached?).");
        }

        using HttpResponseMessage file = await SendAsync(HttpMethod.Get, uri, null, authenticate: false, cancellationToken).ConfigureAwait(false);
        return new SubtitleDownload(link.FileName ?? Path.GetFileName(uri.AbsolutePath), await file.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
    }

    // Only what can be downloaded and is in a wanted language; an episode search by title keeps the series asked for.
    private static IReadOnlyList<SubtitleCandidate> Map(SearchResponse response, SubtitleQuery query)
    {
        string series = ReleaseTitle.CleanTitle(query.Title);
        return
        [
            .. (response.Data ?? [])
                .Select(item => item.Attributes)
                .OfType<Attributes>()
                .Where(a => a.Files is [{ FileId: > 0 }, ..] && a.Language is not null)
                .Where(a => !query.IsEpisode || a.FeatureDetails?.ParentTitle is not string parent || ReleaseTitle.CleanTitle(parent) == series)
                .Select(a => (Attributes: a, Language: IsoLanguages.Find(a.Language!)?.Language))
                .Where(a => a.Language is not null && query.Languages.Contains(a.Language))
                .Select(a => new SubtitleCandidate(
                    ProviderName,
                    a.Attributes.Files![0].FileId.ToString(CultureInfo.InvariantCulture),
                    a.Language!,
                    a.Attributes.Release ?? a.Attributes.Files[0].FileName ?? "",
                    a.Attributes.HearingImpaired,
                    a.Attributes.ForeignPartsOnly,
                    a.Attributes.MoviehashMatch,
                    a.Attributes.DownloadCount)),
        ];
    }

    private static string? Code(Language language) =>
        language == Language.PortugueseBrazil ? "pt-br" : IsoLanguages.Get(language)?.TwoLetterCode;

    private static string? ImdbNumber(string? imdbId) =>
        int.TryParse(imdbId?.Trim().TrimStart('t', 'T'), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 ? id.ToString(CultureInfo.InvariantCulture) : null;

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
        where T : class
    {
        using HttpResponseMessage response = await SendAsync(method, new Uri(url), body, authenticate: true, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException($"OpenSubtitles answered an empty body to {url}.");
    }

    // HTTPS to opensubtitles.com hosts only; a redirect (GET only) is followed by hand when it stays on them. The client caps the
    // response size and the time (ModularrServiceCollectionExtensions).
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, object? body, bool authenticate, CancellationToken cancellationToken)
    {
        for (int redirects = 0; ; redirects++)
        {
            if (!IsOwnHost(uri))
            {
                throw new HttpRequestException($"Refused to contact {uri.GetLeftPart(UriPartial.Authority)}: not an OpenSubtitles HTTPS host.");
            }

            using HttpRequestMessage request = new(method, uri) { Content = body is null ? null : JsonContent.Create(body, options: Json) };
            request.Headers.UserAgent.ParseAdd("Modularr v1.0");
            if (authenticate)
            {
                request.Headers.Add("Api-Key", Account.ApiKey);
                if (await TokenAsync(cancellationToken).ConfigureAwait(false) is string token)
                {
                    request.Headers.Authorization = new("Bearer", token);
                }
            }

            HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is >= HttpStatusCode.MovedPermanently and <= HttpStatusCode.PermanentRedirect && response.Headers.Location is Uri location
                && method == HttpMethod.Get && redirects < MaxRedirects)
            {
                response.Dispose();
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new HttpRequestException($"OpenSubtitles answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
            }

            return response;
        }
    }

    private static bool IsOwnHost(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && (uri.Host.Equals("opensubtitles.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".opensubtitles.com", StringComparison.OrdinalIgnoreCase));

    // With an account, a token from /login raises the download quota; it is kept for TokenLifetime per account.
    private async Task<string?> TokenAsync(CancellationToken cancellationToken)
    {
        OpenSubtitlesOptions account = Account;
        if (string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrEmpty(account.Password))
        {
            return null;
        }

        string key = $"opensubtitles-token:{account.ApiKey}:{account.Username}:{account.Password.GetHashCode(StringComparison.Ordinal)}";
        if (cache.TryGetValue(key, out string? token))
        {
            return token;
        }

        using HttpRequestMessage request = new(HttpMethod.Post, Api + "login") { Content = JsonContent.Create(new { username = account.Username, password = account.Password }, options: Json) };
        request.Headers.UserAgent.ParseAdd("Modularr v1.0");
        request.Headers.Add("Api-Key", account.ApiKey);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenSubtitles login failed ({(int)response.StatusCode}): check the username and password.", null, response.StatusCode);
        }

        token = (await response.Content.ReadFromJsonAsync<LoginResponse>(Json, cancellationToken).ConfigureAwait(false))?.Token;
        return token is null ? null : cache.Set(key, token, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TokenLifetime, Size = 1 });
    }

    private sealed record SearchResponse(IReadOnlyList<Item>? Data);

    private sealed record Item(string? Id, Attributes? Attributes);

    private sealed record Attributes(
        string? Language, int DownloadCount, bool HearingImpaired, bool ForeignPartsOnly, string? Release, bool MoviehashMatch,
        IReadOnlyList<SubtitleFileEntry>? Files, FeatureDetails? FeatureDetails);

    private sealed record SubtitleFileEntry(long FileId, string? FileName);

    private sealed record FeatureDetails(string? ParentTitle);

    private sealed record DownloadResponse(string? Link, string? FileName);

    private sealed record LoginResponse(string? Token);
}
