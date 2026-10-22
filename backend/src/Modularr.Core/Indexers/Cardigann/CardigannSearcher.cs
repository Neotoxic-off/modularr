using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Indexers.Cardigann;

// Searches a public Cardigann definition and resolves its download links (Prowlarr Cardigann + HttpIndexerBase fetch).
// Site and definition problems come back as a typed failure; only caller cancellation throws.
public sealed partial class CardigannSearcher(HttpClient httpClient, IOptions<ModularrOptions> options)
{
    private const int MaxRedirects = 5;

    private static readonly string[] CommonWords = ["and", "the", "an", "of"];

    // Login, captcha and cookie flows are not ported: only public sites without a login block can be searched.
    public static bool IsSupported(CardigannDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Privacy == IndexerPrivacy.Public && definition.Login is null;
    }

    /// <param name="baseUrl">The configured site URL; null or a legacy link uses the definition's first link.</param>
    public async Task<CardigannSearchResult> SearchAsync(CardigannDefinition definition, string? baseUrl, CardigannSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!IsSupported(definition))
        {
            return new CardigannSearchResult.Failed(Unsupported(definition));
        }

        try
        {
            CardigannSite site = new(definition, baseUrl);
            List<ParsedRelease> parsed = [];
            IReadOnlyList<CardigannRequest> requests = CardigannRequestGenerator.Build(site, query);
            for (int i = 0; i < requests.Count; i++)
            {
                // shortcut: requestDelay only spaces the requests of one search, add a per-site limiter when searches run in parallel.
                if (i > 0 && definition.RequestDelay is > 0 and double delay)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken).ConfigureAwait(false);
                }

                CardigannRequest request = requests[i];
                Page page = await SendAsync(site, request.Url, request.Method, request.Form, request.Headers, request.SearchPath.FollowRedirect, referer: null, cancellationToken).ConfigureAwait(false);
                parsed.AddRange(CardigannParser.Parse(site, request with { Url = page.Url }, page.Content));
            }

            if (definition.Search.Rows.Filters?.Any(filter => filter.Name == "andmatch") == true)
            {
                parsed = [.. FilterByQuery(parsed, query)];
            }

            return new CardigannSearchResult.Found([.. parsed.Select(release => ToReleaseInfo(release, definition.Name)).OfType<ReleaseInfo>().DistinctBy(release => release.Id)]);
        }
        catch (FailureException ex)
        {
            return new CardigannSearchResult.Failed(ex.Failure);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A definition this engine cannot evaluate (bad template, regex or setting) or an unreadable response.
            return new CardigannSearchResult.Failed(new CardigannFailure(CardigannFailureKind.InvalidResponse, $"{definition.Id}: {ex.Message}"));
        }
    }

    // Prowlarr CardigannRequestGenerator.DownloadRequest: follows the definition's download block from a release's download URL
    // (usually its details page) to the final .torrent URL or magnet. Without a download block the link is already final.
    public async Task<CardigannDownloadResult> ResolveDownloadAsync(CardigannDefinition definition, string? baseUrl, Uri link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!IsSupported(definition))
        {
            return new CardigannDownloadResult.Failed(Unsupported(definition));
        }

        if (definition.Download is not { } download)
        {
            return new CardigannDownloadResult.Resolved(link);
        }

        // shortcut: a POST download returns a link the caller would have to POST to, report it until the download client takes requests.
        if (string.Equals(download.Method, "post", StringComparison.OrdinalIgnoreCase))
        {
            return new CardigannDownloadResult.Failed(new CardigannFailure(CardigannFailureKind.Unsupported, $"{definition.Id}: POST downloads are not supported."));
        }

        try
        {
            CardigannSite site = new(definition, baseUrl);
            Dictionary<string, object?> variables = site.BaseVariables();
            CardigannSite.AddUriVariables(variables, link, ".DownloadUri");
            Dictionary<string, string> headers = CardigannSite.Headers(download.Headers ?? definition.Search.Headers, variables);

            Task<Page> GetAsync(Uri url, Uri? referer = null) => SendAsync(site, url, HttpMethod.Get, [], headers, followRedirect: true, referer, cancellationToken);

            Page? page = null;
            if (download.Before is { } before)
            {
                string? path = before.Path;
                if (before.PathSelector is { } pathSelector)
                {
                    page = await GetAsync(link).ConfigureAwait(false);
                    path = MatchSelector(site, page.Content, pathSelector, variables);
                }

                page = await SendBeforeAsync(site, before with { Path = path }, variables, headers, link, cancellationToken).ConfigureAwait(false);
            }

            if (download.InfoHash is { } infoHash)
            {
                try
                {
                    if (!infoHash.UseBeforeResponse || download.Before is null || page is null)
                    {
                        page = await GetAsync(link).ConfigureAwait(false);
                    }

                    string hash = MatchSelector(site, page.Content, infoHash.Hash, variables) ?? throw new InvalidDataException("InfoHash selectors didn't match hash.");
                    string title = MatchSelector(site, page.Content, infoHash.Title, variables) ?? throw new InvalidDataException("InfoHash selectors didn't match title.");
                    if (CardigannParser.BuildMagnetLink(hash, title) is { } magnet)
                    {
                        return new CardigannDownloadResult.Resolved(new Uri(magnet));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Upstream logs the failed info hash block and downloads the link itself.
                }

                return new CardigannDownloadResult.Resolved(link);
            }

            if (download.Selectors is not { Count: > 0 } selectors)
            {
                return new CardigannDownloadResult.Resolved(link);
            }

            CardigannFailure? lastFailure = null;
            foreach (SelectorField selector in selectors)
            {
                try
                {
                    if (!selector.UseBeforeResponse || download.Before is null || page is null)
                    {
                        page = await GetAsync(link).ConfigureAwait(false);
                    }

                    if (MatchSelector(site, page.Content, selector, variables) is not { } href)
                    {
                        continue;
                    }

                    Uri torrent = site.ResolvePath(href, link);

                    // A broken mirror serves an HTML page instead of a bencoded torrent: try the next selector.
                    if (torrent.Scheme != "magnet" && definition.TestLinkTorrent)
                    {
                        page = await GetAsync(torrent, link).ConfigureAwait(false);
                        if (page.Content.Length >= 1 && page.Content[0] != 'd')
                        {
                            continue;
                        }
                    }

                    return new CardigannDownloadResult.Resolved(torrent);
                }
                catch (FailureException ex)
                {
                    lastFailure = ex.Failure;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Upstream logs a failing selector and tries the next one.
                }
            }

            return new CardigannDownloadResult.Failed(lastFailure ?? new CardigannFailure(CardigannFailureKind.DownloadNotFound, $"Download selectors didn't match for {link}"));
        }
        catch (FailureException ex)
        {
            return new CardigannDownloadResult.Failed(ex.Failure);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CardigannDownloadResult.Failed(new CardigannFailure(CardigannFailureKind.InvalidResponse, $"{definition.Id}: {ex.Message}"));
        }
    }

    private static CardigannFailure Unsupported(CardigannDefinition definition) =>
        new(CardigannFailureKind.Unsupported, $"{definition.Id}: only public indexers without login are supported.");

    // Upstream HandleRequest: the download block's "before" request (GET inputs in the query, POST inputs as a form).
    private Task<Page> SendBeforeAsync(CardigannSite site, BeforeBlock before, Dictionary<string, object?> variables, Dictionary<string, string> headers, Uri referer, CancellationToken cancellationToken)
    {
        string url = site.ResolvePath(CardigannTemplate.Apply(before.Path, variables)).AbsoluteUri;
        bool isPost = string.Equals(before.Method, "post", StringComparison.OrdinalIgnoreCase);
        List<KeyValuePair<string, string>> inputs = [.. (before.Inputs ?? new Dictionary<string, string>()).Select(input => new KeyValuePair<string, string>(input.Key, CardigannTemplate.Apply(input.Value, variables) ?? ""))];

        if (!isPost && inputs.Count > 0)
        {
            url += (url.Contains('?', StringComparison.Ordinal) ? before.QuerySeparator : "?")
                + string.Join(before.QuerySeparator, inputs.Select(input => $"{input.Key}={CardigannFilters.UrlEncode(input.Value, site.Encoding)}"));
        }

        return SendAsync(site, new Uri(url), isPost ? HttpMethod.Post : HttpMethod.Get, isPost ? inputs : [], headers, followRedirect: true, referer, cancellationToken);
    }

    // Upstream MatchSelector: first element of the page matching the selector, its attribute or text, then filters.
    private static string? MatchSelector(CardigannSite site, string content, SelectorField? selector, Dictionary<string, object?> variables)
    {
        if (selector is null)
        {
            return null;
        }

        using IDocument document = new HtmlParser().ParseDocument(content);
        if (document.QuerySelector(CardigannTemplate.Apply(selector.Selector, variables) ?? "") is not { } element)
        {
            return null;
        }

        string value = selector.Attribute is null
            ? element.TextContent
            : element.GetAttribute(selector.Attribute) ?? throw new InvalidDataException($"Attribute \"{selector.Attribute}\" is not set");
        return CardigannFilters.Apply(value, selector.Filters, variables, site.Encoding);
    }

    private async Task<Page> SendAsync(
        CardigannSite site,
        Uri url,
        HttpMethod method,
        IReadOnlyList<KeyValuePair<string, string>> form,
        IReadOnlyDictionary<string, string> headers,
        bool followRedirect,
        Uri? referer,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.Indexers.RequestTimeout);
        try
        {
            for (int redirects = 0; ; redirects++)
            {
                using HttpRequestMessage request = new(method, url);
                foreach ((string name, string value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }

                if (referer is not null)
                {
                    request.Headers.Referrer = referer;
                }

                if (method == HttpMethod.Post)
                {
                    // Form values are encoded in the site's charset, like upstream.
                    request.Content = new StringContent(string.Join("&", form.Select(pair => $"{pair.Key}={CardigannFilters.UrlEncode(pair.Value, site.Encoding)}")), site.Encoding, "application/x-www-form-urlencoded");
                }

                using HttpResponseMessage response = await httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
                string content = site.Encoding.GetString(await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false));

                int status = (int)response.StatusCode;
                if (status is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    Uri target = new(url, location);
                    if (!followRedirect || redirects >= MaxRedirects)
                    {
                        throw new FailureException(CardigannFailureKind.Redirected, $"Redirected to {target} from indexer request");
                    }

                    url = target;
                    method = HttpMethod.Get;
                    form = [];
                    continue;
                }

                if (IsCloudflareProtected(response, content))
                {
                    throw new FailureException(CardigannFailureKind.CloudflareProtected, $"Cloudflare protection detected for {url.Host}, FlareSolverr may be required.");
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new FailureException(CardigannFailureKind.HttpError, $"Unexpected response status {status} from {url}");
                }

                return new Page(url, content);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FailureException(CardigannFailureKind.Timeout, $"{url.Host} did not answer within {options.Value.Indexers.RequestTimeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException ex)
        {
            throw new FailureException(CardigannFailureKind.HttpError, $"{url.Host}: {ex.Message}");
        }
    }

    // Prowlarr CloudFlareDetectionService: a challenge page served by Cloudflare or DDoS-Guard.
    private static bool IsCloudflareProtected(HttpResponseMessage response, string content)
    {
        bool protectedServer = response.Headers.TryGetValues("Server", out IEnumerable<string>? servers)
            && servers.Any(server => server.ToLowerInvariant() is "cloudflare" or "cloudflare-nginx" or "ddos-guard");
        if (!protectedServer)
        {
            return false;
        }

        if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden
            && (content.Contains("<title>Just a moment...</title>", StringComparison.Ordinal)
                || content.Contains("<title>Access denied</title>", StringComparison.Ordinal)
                || content.Contains("<title>Attention Required! | Cloudflare</title>", StringComparison.Ordinal)
                || content.Trim() == "error code: 1020"
                || content.Contains("<title>DDOS-GUARD</title>", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Custom Cloudflare setups (EbookParadijs, Film-Paleis, ...).
        return string.Join(",", response.Headers.Vary) == "Accept-Encoding,User-Agent"
            && response.Content.Headers.ContentEncoding.Count == 0
            && content.Contains("ddos", StringComparison.OrdinalIgnoreCase);
    }

    // Prowlarr IndexerBase.FilterReleasesByQuery ("andmatch"): keyword searches keep releases containing the search terms.
    private static IEnumerable<ParsedRelease> FilterByQuery(IEnumerable<ParsedRelease> releases, CardigannSearchQuery query)
    {
        if (query.IsRssSearch || query.IsIdSearch)
        {
            return releases;
        }

        string[] terms = [.. NonWordRegex().Split(query.Term ?? "").Where(term => term.Length > 1 && !CommonWords.Contains(term, StringComparer.InvariantCultureIgnoreCase))];
        return releases.Where(release =>
        {
            int matches = terms.Count(term => (release.Title?.Contains(term, StringComparison.InvariantCultureIgnoreCase) ?? false)
                || (release.Description?.Contains(term, StringComparison.InvariantCultureIgnoreCase) ?? false));

            // Upstream: more than one term needs at least two hits.
            return terms.Length > 1 ? matches > 1 : matches > 0;
        });
    }

    // Rows without a title or anything to download are of no use to Sonarr/Radarr.
    private static ReleaseInfo? ToReleaseInfo(ParsedRelease release, string indexer) =>
        string.IsNullOrWhiteSpace(release.Title) || (release.DownloadUrl ?? release.MagnetUrl) is null
            ? null
            : new ReleaseInfo
            {
                Id = release.Guid ?? release.DownloadUrl ?? release.MagnetUrl!,
                Title = release.Title,
                Indexer = indexer,
                PublishDate = release.PublishDate.Year < 2 ? default : new DateTimeOffset(release.PublishDate),
                Size = release.Size,
                DownloadUrl = release.DownloadUrl,
                MagnetUrl = release.MagnetUrl,
                InfoHash = release.InfoHash,
                InfoUrl = release.InfoUrl,
                Seeders = release.Seeders,
                Peers = release.Peers,
                Grabs = release.Grabs,
                DownloadVolumeFactor = release.DownloadVolumeFactor,
                UploadVolumeFactor = release.UploadVolumeFactor,
                Categories = release.Categories,
                ImdbId = release.ImdbId,
                TmdbId = release.TmdbId,
                TvdbId = release.TvdbId,
            };

    [GeneratedRegex(@"[^\w]+")]
    private static partial Regex NonWordRegex();

    private sealed record Page(Uri Url, string Content);

    // Carries a typed failure out of nested HTTP helpers to the public entry points, which turn it into a result.
    private sealed class FailureException(CardigannFailureKind kind, string message) : Exception(message)
    {
        public CardigannFailure Failure { get; } = new(kind, message);
    }
}
