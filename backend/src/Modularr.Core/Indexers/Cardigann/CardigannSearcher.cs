using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers.Definitions;

namespace Modularr.Core.Indexers.Cardigann;

// Searches a Cardigann definition and resolves its download links (Prowlarr Cardigann + HttpIndexerBase fetch), logging in
// first when the definition has a login block. Site and definition problems come back as a typed failure; only caller
// cancellation throws.
public sealed partial class CardigannSearcher(HttpClient httpClient, IOptionsMonitor<ModularrOptions> options, CardigannSessions sessions)
{
    private const int MaxRedirects = 5;

    private static readonly string[] CommonWords = ["and", "the", "an", "of"];

    // Any site runs once the credentials its templates read are filled in; most public ones need none.
    public static bool IsSupported(CardigannDefinition definition, IReadOnlyDictionary<string, string>? values = null) =>
        CardigannSettings.IsConfigured(definition, values);

    /// <param name="baseUrl">The configured site URL; null or a legacy link uses the definition's first link.</param>
    public async Task<CardigannSearchResult> SearchAsync(CardigannDefinition definition, string? baseUrl, CardigannSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyDictionary<string, string>? values = options.CurrentValue.Indexers.Settings.GetValueOrDefault(definition.Id);
        if (!IsSupported(definition, values))
        {
            return new CardigannSearchResult.Failed(Unsupported(definition));
        }

        try
        {
            CardigannSite site = new(definition, baseUrl, values, sessions);
            await EnsureLoggedInAsync(site, cancellationToken).ConfigureAwait(false);
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
                Task<Page?> SearchOnceAsync() => SendLoggedInAsync(site, request.Url, request.Method, request.Form, request.Headers, request.SearchPath.FollowRedirect, cancellationToken);

                // Prowlarr HttpIndexerBase.FetchPage: a response asking to log in triggers one fresh login, then the request again.
                Page? page = await SearchOnceAsync().ConfigureAwait(false);
                if (page is null)
                {
                    await LoginAsync(site, cancellationToken).ConfigureAwait(false);
                    page = await SearchOnceAsync().ConfigureAwait(false) ?? throw new FailureException(CardigannFailureKind.LoginFailed,
                        $"{definition.Id}: the site still asks to log in after a fresh login; check the credentials or cookie.");
                }

                parsed.AddRange(CardigannParser.Parse(site, request with { Url = page.Url }, page.Content));
            }

            if (definition.Search.Rows.Filters?.Any(filter => filter.Name == "andmatch") == true)
            {
                parsed = [.. FilterByQuery(parsed, query)];
            }

            return new CardigannSearchResult.Found([.. parsed.Select(release => ToReleaseInfo(release, definition)).OfType<ReleaseInfo>().DistinctBy(release => release.Id)]);
        }
        catch (FailureException ex)
        {
            return new CardigannSearchResult.Failed(Scrubbed(ex.Failure, definition, values));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A definition this engine cannot evaluate (bad template, regex or setting) or an unreadable response.
            return new CardigannSearchResult.Failed(Scrubbed(new CardigannFailure(CardigannFailureKind.InvalidResponse, $"{definition.Id}: {ex.Message}"), definition, values));
        }
    }

    // Prowlarr CardigannRequestGenerator.DownloadRequest: follows the definition's download block from a release's download URL
    // (usually its details page) to the final .torrent URL or magnet. Without a download block the link is already final.
    public async Task<CardigannDownloadResult> ResolveDownloadAsync(CardigannDefinition definition, string? baseUrl, Uri link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(link);
        IReadOnlyDictionary<string, string>? values = options.CurrentValue.Indexers.Settings.GetValueOrDefault(definition.Id);
        if (!IsSupported(definition, values))
        {
            return new CardigannDownloadResult.Failed(Unsupported(definition));
        }

        // shortcut: a POST download returns a link the caller would have to POST to, report it until the download client takes requests.
        if (string.Equals(definition.Download?.Method, "post", StringComparison.OrdinalIgnoreCase))
        {
            return new CardigannDownloadResult.Failed(new CardigannFailure(CardigannFailureKind.Unsupported, $"{definition.Id}: POST downloads are not supported."));
        }

        try
        {
            CardigannSite site = new(definition, baseUrl, values, sessions);

            // The .torrent itself is fetched by the caller with this session's cookies (CardigannSessions.CookieHeader).
            await EnsureLoggedInAsync(site, cancellationToken).ConfigureAwait(false);
            if (definition.Download is not { } download)
            {
                return new CardigannDownloadResult.Resolved(link);
            }

            Dictionary<string, object?> variables = site.BaseVariables();
            CardigannSite.AddUriVariables(variables, link, ".DownloadUri");
            Dictionary<string, string> headers = CardigannSite.Headers(download.Headers ?? definition.Search.Headers, variables);

            Task<Page> GetAsync(Uri url, Uri? referer = null) => SendAsync(site, url, HttpMethod.Get, [], headers, followRedirect: true, referer, login: false, cancellationToken);

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

            return new CardigannDownloadResult.Failed(Scrubbed(lastFailure ?? new CardigannFailure(CardigannFailureKind.DownloadNotFound, $"Download selectors didn't match for {link}"), definition, values));
        }
        catch (FailureException ex)
        {
            return new CardigannDownloadResult.Failed(Scrubbed(ex.Failure, definition, values));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CardigannDownloadResult.Failed(Scrubbed(new CardigannFailure(CardigannFailureKind.InvalidResponse, $"{definition.Id}: {ex.Message}"), definition, values));
        }
    }

    private static CardigannFailure Unsupported(CardigannDefinition definition) =>
        new(CardigannFailureKind.Unsupported, $"{definition.Id}: fill in its login settings first.");

    private static CardigannFailure Scrubbed(CardigannFailure failure, CardigannDefinition definition, IReadOnlyDictionary<string, string>? values) =>
        failure with { Message = CardigannSettings.Scrub(failure.Message, definition, values) };

    // No session yet (first use, restart, new settings): log in before the first request instead of after a failed one.
    private Task EnsureLoggedInAsync(CardigannSite site, CancellationToken cancellationToken) =>
        site.Cookies.IsEmpty && (site.Definition.Login is not null || site.Values.ContainsKey("cookie"))
            ? LoginAsync(site, cancellationToken)
            : Task.CompletedTask;

    // A failed login leaves no half session behind: the next request logs in again.
    private async Task LoginAsync(CardigannSite site, CancellationToken cancellationToken)
    {
        try
        {
            await LoginCoreAsync(site, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            site.Cookies.Clear();
            throw;
        }
    }

    // Prowlarr CardigannRequestGenerator.DoLogin. A user cookie stands for the login (cookie method, or a site behind a captcha).
    private async Task LoginCoreAsync(CardigannSite site, CancellationToken cancellationToken)
    {
        CardigannDefinition definition = site.Definition;
        LoginBlock? login = definition.Login;
        Dictionary<string, object?> variables = site.BaseVariables();
        Dictionary<string, string> headers = CardigannSite.Headers(login?.Headers ?? definition.Search.Headers, variables);
        site.Cookies.Clear();
        foreach (string cookie in login?.Cookies ?? [])
        {
            CardigannSessions.AddHeader(site.Cookies, cookie);
        }

        if (site.Values.GetValueOrDefault("cookie") is { Length: > 0 } userCookie)
        {
            CardigannSessions.AddHeader(site.Cookies, userCookie);
        }
        else if (login is not null)
        {
            Uri loginUrl = site.ResolvePath(CardigannTemplate.Apply(login.Path, variables));
            List<KeyValuePair<string, string>> inputs = [.. (login.Inputs ?? new Dictionary<string, string>()).Select(input => new KeyValuePair<string, string>(input.Key, CardigannTemplate.Apply(input.Value, variables) ?? ""))];
            Page result = login.Method switch
            {
                "post" => await SendAsync(site, loginUrl, HttpMethod.Post, inputs, headers, followRedirect: true, site.SiteLink, login: true, cancellationToken).ConfigureAwait(false),
                "form" => await SubmitFormAsync(site, loginUrl, variables, headers, cancellationToken).ConfigureAwait(false),
                "get" => await SendAsync(site, new Uri(loginUrl.AbsoluteUri + "?" + string.Join("&", inputs.Select(input => $"{input.Key}={CardigannFilters.UrlEncode(input.Value, site.Encoding)}"))),
                    HttpMethod.Get, [], headers, followRedirect: true, site.SiteLink, login: true, cancellationToken).ConfigureAwait(false),
                "oneurl" => await SendAsync(site, site.ResolvePath(CardigannTemplate.Apply(login.Path, variables) + CardigannTemplate.Apply(login.Inputs?.GetValueOrDefault("oneurl"), variables)),
                    HttpMethod.Get, [], headers, followRedirect: true, site.SiteLink, login: true, cancellationToken).ConfigureAwait(false),
                "cookie" => throw new FailureException(CardigannFailureKind.LoginFailed, $"{definition.Id}: paste a session cookie from your browser in its login settings."),
                _ => throw new FailureException(CardigannFailureKind.Unsupported, $"{definition.Id}: login method {login.Method} is not supported."),
            };
            CheckForError(site, result, login.Error, variables);
        }

        // login.test: a page only a logged-in user sees, checked like a search response.
        if (login?.Test?.Path is { } testPath
            && await SendLoggedInAsync(site, site.ResolvePath(CardigannTemplate.Apply(testPath, variables)), HttpMethod.Get, [], headers, followRedirect: true, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new FailureException(CardigannFailureKind.LoginFailed, $"{definition.Id}: login did not succeed (the login test page still asks to log in); check the credentials or cookie.");
        }
    }

    // The "form" login: fetch the login page, take its form's inputs, merge the definition's, post to the form action.
    private async Task<Page> SubmitFormAsync(CardigannSite site, Uri loginUrl, Dictionary<string, object?> variables, Dictionary<string, string> headers, CancellationToken cancellationToken)
    {
        CardigannDefinition definition = site.Definition;
        LoginBlock login = definition.Login!;
        Page landing = await SendAsync(site, loginUrl, HttpMethod.Get, [], headers, followRedirect: true, site.SiteLink, login: true, cancellationToken).ConfigureAwait(false);
        using IDocument document = new HtmlParser().ParseDocument(landing.Content);

        // Captchas are not solved (Prowlarr asks the user for each login); a session cookie from a browser skips the login.
        if ((login.Captcha is { } captcha && (captcha.Selector is null || document.QuerySelector(captcha.Selector) is not null))
            || document.QuerySelector("script[src*=\"simpleCaptcha\"]") is not null)
        {
            throw new FailureException(CardigannFailureKind.LoginFailed, definition.Settings.Any(field => field.Name == "cookie")
                ? $"{definition.Id}: this site needs a captcha: paste a session cookie instead."
                : $"{definition.Id}: this site needs a captcha at login, which modularr cannot solve.");
        }

        string formSelector = login.Form ?? "form";
        IElement form = document.QuerySelector(formSelector)
            ?? throw new FailureException(CardigannFailureKind.LoginFailed, $"{definition.Id}: login failed: no form found on {loginUrl} using selector {formSelector}.");

        Dictionary<string, string> pairs = new(StringComparer.Ordinal);
        foreach (IElement input in form.QuerySelectorAll("input"))
        {
            if (input.GetAttribute("name") is not { } name || input.HasAttribute("disabled")
                || (input is IHtmlInputElement { Type: "checkbox" or "radio", IsChecked: false }))
            {
                continue;
            }

            pairs[name] = input.GetAttribute("value") ?? "";
        }

        foreach ((string key, string value) in login.Inputs ?? new Dictionary<string, string>())
        {
            string name = !login.Selectors ? key
                : document.QuerySelector(key)?.GetAttribute("name") ?? throw new FailureException(CardigannFailureKind.LoginFailed, $"{definition.Id}: login failed: no input found using selector {key}.");
            pairs[name] = CardigannTemplate.Apply(value, variables) ?? "";
        }

        foreach ((string key, SelectorBlock block) in login.SelectorInputs ?? new Dictionary<string, SelectorBlock>())
        {
            if (SelectInput(site, key, block, document, variables) is { } value)
            {
                pairs[key] = value;
            }
        }

        string[] query = [.. (login.GetSelectorInputs ?? new Dictionary<string, SelectorBlock>())
            .Select(input => (input.Key, Value: SelectInput(site, input.Key, input.Value, document, variables)))
            .Where(input => input.Value is not null)
            .Select(input => $"{Uri.EscapeDataString(input.Key)}={Uri.EscapeDataString(input.Value!)}")];
        string submit = (login.SubmitPath ?? form.GetAttribute("action") ?? "") + (query.Length > 0 ? "?" + string.Join("&", query) : "");

        // shortcut: a multipart/form-data form is posted URL-encoded, port Prowlarr's multipart body if a site refuses it.
        return await SendAsync(site, site.ResolvePath(submit, loginUrl), HttpMethod.Post, [.. pairs], headers, followRedirect: true, loginUrl, login: true, cancellationToken).ConfigureAwait(false);
    }

    private static string? SelectInput(CardigannSite site, string key, SelectorBlock block, IDocument document, Dictionary<string, object?> variables)
    {
        try
        {
            return CardigannParser.HtmlSelect(site, block, document.DocumentElement, variables, required: !block.Optional);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException)
        {
            throw new FailureException(CardigannFailureKind.LoginFailed, $"{site.Definition.Id}: login failed: selector input {key}: {ex.Message}");
        }
    }

    // Prowlarr CheckForError: the first login.error selector found on the answer is the failure, with its message.
    private static void CheckForError(CardigannSite site, Page result, IReadOnlyList<ErrorBlock>? errors, Dictionary<string, object?> variables)
    {
        using IDocument document = new HtmlParser().ParseDocument(result.Content);
        foreach (ErrorBlock error in errors ?? [])
        {
            if (error.Selector is not null && document.QuerySelector(error.Selector) is { } selection)
            {
                string message = (error.Message is null ? null : CardigannParser.HtmlSelect(site, error.Message, document.DocumentElement, variables, required: false)) ?? selection.TextContent;
                throw new FailureException(CardigannFailureKind.LoginFailed, $"{site.Definition.Id}: login failed: {message.Trim()}");
            }
        }
    }

    // Prowlarr CheckIfLoginIsNeeded: with a login block, a redirect, an HTTP error status or an HTML page without the login
    // test selector means the session is gone (null); without one the request fails as usual.
    private async Task<Page?> SendLoggedInAsync(
        CardigannSite site, Uri url, HttpMethod method, IReadOnlyList<KeyValuePair<string, string>> form, IReadOnlyDictionary<string, string> headers, bool followRedirect, CancellationToken cancellationToken)
    {
        LoginBlock? login = site.Definition.Login;
        try
        {
            Page page = await SendAsync(site, url, method, form, headers, followRedirect, referer: null, login: false, cancellationToken).ConfigureAwait(false);
            if (login?.Test?.Selector is not { } selector || page.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == false)
            {
                return page;
            }

            using IDocument document = new HtmlParser().ParseDocument(page.Content);
            return document.QuerySelectorAll(selector).Length == 0 ? null : page;
        }
        catch (FailureException ex) when (login is not null && (ex.Failure.Kind == CardigannFailureKind.Redirected || ex.Status is not null))
        {
            return null;
        }
    }

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

        return SendAsync(site, new Uri(url), isPost ? HttpMethod.Post : HttpMethod.Get, isPost ? inputs : [], headers, followRedirect: true, referer, login: false, cancellationToken);
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
        bool login,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.CurrentValue.Indexers.RequestTimeout);
        try
        {
            for (int redirects = 0; ; redirects++)
            {
                using HttpRequestMessage request = new(method, url);
                foreach ((string name, string value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }

                // Session cookies go to the site (and its subdomains) only, on every hop of a redirect.
                bool sameSite = url.Host.Equals(site.SiteLink.Host, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("." + site.SiteLink.Host, StringComparison.OrdinalIgnoreCase);
                if (sameSite && !site.Cookies.IsEmpty)
                {
                    request.Headers.TryAddWithoutValidation("Cookie", CardigannSessions.Format(site.Cookies));
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

                if (sameSite && response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
                {
                    foreach (string[] cookie in setCookies.Select(header => header.Split(';', 2)[0].Split('=', 2)).Where(pair => pair.Length == 2 && pair[0].Trim().Length > 0))
                    {
                        // An emptied or "deleted" value is how a site removes a cookie (logout, expired session).
                        if (cookie[1].Trim() is "" or "deleted")
                        {
                            site.Cookies.TryRemove(cookie[0].Trim(), out _);
                        }
                        else
                        {
                            site.Cookies[cookie[0].Trim()] = cookie[1].Trim();
                        }
                    }
                }

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

                // A login answer is checked by its error selectors instead (upstream SuppressHttpError), except a plain refusal.
                if (login && response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new FailureException(CardigannFailureKind.LoginFailed, $"{site.Definition.Id}: the site refused the login (HTTP 401); check the credentials.");
                }

                if (response.StatusCode != HttpStatusCode.OK && !login)
                {
                    throw new FailureException(CardigannFailureKind.HttpError, $"Unexpected response status {status} from {url}", status);
                }

                return new Page(url, content, response.Content.Headers.ContentType?.MediaType);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FailureException(CardigannFailureKind.Timeout, $"{url.Host} did not answer within {options.CurrentValue.Indexers.RequestTimeout.TotalSeconds:0} s.");
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
    private static ReleaseInfo? ToReleaseInfo(ParsedRelease release, CardigannDefinition definition) =>
        string.IsNullOrWhiteSpace(release.Title) || (release.DownloadUrl ?? release.MagnetUrl) is null
            ? null
            : new ReleaseInfo
            {
                Id = release.Guid ?? release.DownloadUrl ?? release.MagnetUrl!,
                Title = release.Title,
                Indexer = definition.Name,
                IndexerId = definition.Id,
                // A date before year 2 or more than a day ahead is the site's mistake (or a poisoned value): treat it as unknown.
                PublishDate = release.PublishDate.Year < 2 || release.PublishDate > DateTime.Now.AddDays(1) ? default : new DateTimeOffset(release.PublishDate),
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

    private sealed record Page(Uri Url, string Content, string? MediaType = null);

    // Carries a typed failure out of nested HTTP helpers to the public entry points, which turn it into a result.
    private sealed class FailureException(CardigannFailureKind kind, string message, int? status = null) : Exception(message)
    {
        public CardigannFailure Failure { get; } = new(kind, message);

        // The HTTP status of an unexpected answer, which for a logged-in site means the session is gone.
        public int? Status { get; } = status;
    }
}
