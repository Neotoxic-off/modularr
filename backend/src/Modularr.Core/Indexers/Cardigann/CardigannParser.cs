using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Xml.Parser;
using Modularr.Core.Indexers.Definitions;
using MonoTorrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Modularr.Core.Indexers.Cardigann;

// Prowlarr CardigannParser: turns a search response (HTML, XML or JSON) into releases using the definition's rows and fields.
internal static partial class CardigannParser
{
    // Missing ids, artwork and descriptions never invalidate a row.
    private static readonly FrozenSet<string> OptionalFields = FrozenSet.Create(StringComparer.Ordinal,
        "imdb", "imdbid", "tmdbid", "rageid", "tvdbid", "tvmazeid", "traktid", "doubanid", "poster", "banner", "description", "genre");

    // Upstream builds public magnets with this tracker list.
    private static readonly string[] Trackers =
    [
        "http://tracker.opentrackr.org:1337/announce", "udp://tracker.auctor.tv:6969/announce", "udp://opentracker.i2p.rocks:6969/announce",
        "https://opentracker.i2p.rocks:443/announce", "udp://open.demonii.com:1337/announce", "udp://tracker.openbittorrent.com:6969/announce",
        "http://tracker.openbittorrent.com:80/announce", "udp://open.stealth.si:80/announce", "udp://tracker.torrent.eu.org:451/announce",
        "udp://tracker.moeking.me:6969/announce", "udp://explodie.org:6969/announce", "udp://exodus.desync.com:6969/announce",
        "udp://uploads.gamecoast.net:6969/announce", "udp://tracker1.bt.moack.co.kr:80/announce", "udp://tracker.tiny-vps.com:6969/announce",
        "udp://tracker.theoks.net:6969/announce", "udp://tracker.skyts.net:6969/announce", "udp://tracker-udp.gbitt.info:80/announce",
        "udp://open.tracker.ink:6969/announce", "udp://movies.zsw.ca:6969/announce",
    ];

    /// <exception cref="InvalidDataException">The response as a whole cannot be read (invalid JSON, rows selector not found).</exception>
    public static IReadOnlyList<ParsedRelease> Parse(CardigannSite site, CardigannRequest request, string content)
    {
        List<ParsedRelease> releases = request.SearchPath.Response?.Type == "json"
            ? ParseJson(site, request, content)
            : ParseDocument(site, request, content);

        foreach (ParsedRelease release in releases)
        {
            // Public sites only: a magnet is built from the info hash, and the hash read back from a magnet.
            if (release.MagnetUrl is null && !string.IsNullOrWhiteSpace(release.InfoHash) && site.Definition.Type != "private")
            {
                release.MagnetUrl = BuildMagnetLink(release.InfoHash, release.Title);
            }

            if (release.MagnetUrl is not null && string.IsNullOrWhiteSpace(release.InfoHash))
            {
                release.InfoHash = InfoHashFromMagnet(release.MagnetUrl);
            }
        }

        return releases;
    }

    public static string? BuildMagnetLink(string infoHash, string? title)
    {
        try
        {
            return new MagnetLink(InfoHash.FromHex(infoHash), title, Trackers).ToV1String();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // Not a 40-character hex v1 hash.
            return null;
        }
    }

    private static string? InfoHashFromMagnet(string magnet)
    {
        try
        {
            return ParseUtil.GetArgumentFromQueryString(magnet, "xt")?.Split(':')[^1];
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static List<ParsedRelease> ParseJson(CardigannSite site, CardigannRequest request, string content)
    {
        SearchBlock search = site.Definition.Search;
        Dictionary<string, object?> variables = request.Variables;
        string? noResultsMessage = request.SearchPath.Response?.NoResultsMessage;
        if (noResultsMessage is not null && (!string.IsNullOrWhiteSpace(noResultsMessage) ? content.Contains(noResultsMessage, StringComparison.Ordinal) : string.IsNullOrWhiteSpace(content)))
        {
            return [];
        }

        JToken json;
        try
        {
            // Dates stay strings: the date field parses them like any scraped text.
            using JsonTextReader reader = new(new StringReader(content)) { DateParseHandling = DateParseHandling.None };
            json = JToken.ReadFrom(reader);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException("Error Parsing Json Response", ex);
        }

        if (search.Rows.Count is not null)
        {
            try
            {
                if (int.TryParse(JsonSelect(site, search.Rows.Count, json, variables, required: true), out int count) && count < 1)
                {
                    return [];
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException)
            {
                // Upstream ignores an unreadable count.
            }
        }

        List<JToken>? rows = JsonRows(json, CardigannTemplate.Apply(search.Rows.Selector, variables) ?? "");
        if (rows is null)
        {
            return search.Rows.MissingAttributeEqualsNoResults ? [] : throw new InvalidDataException("Error Parsing Rows Selector");
        }

        List<ParsedRelease> releases = [];
        foreach (JToken row in rows)
        {
            JToken? container = search.Rows.Attribute is null ? row : row.SelectToken(search.Rows.Attribute);
            if (container is null or { Type: JTokenType.Null })
            {
                continue;
            }

            IEnumerable<JToken> items = search.Rows.Multiple ? container.Children() : [container];
            foreach (JToken item in items)
            {
                ParsedRelease release = new();
                bool rowFailed = false;
                foreach (SearchField field in search.Fields)
                {
                    // ".." reads from the row above a "multiple" attribute (e.g. the movie of a YTS torrent).
                    JToken parent = field.Block.Selector?.StartsWith("..", StringComparison.Ordinal) == true ? row : item;
                    rowFailed |= !ParseField(site, request, release, field, variables, required => JsonSelect(site, field.Block, parent, variables, required));
                }

                // Upstream fails the whole response on a required JSON field; one bad row only drops that row here.
                if (!rowFailed)
                {
                    releases.Add(release);
                }
            }
        }

        return releases;
    }

    private static List<ParsedRelease> ParseDocument(CardigannSite site, CardigannRequest request, string content)
    {
        SearchBlock search = site.Definition.Search;
        Dictionary<string, object?> variables = request.Variables;
        if (search.PreprocessingFilters is not null)
        {
            content = CardigannFilters.Apply(content, search.PreprocessingFilters, variables, site.Encoding);
        }

        using IDocument document = request.SearchPath.Response?.Type == "xml"
            ? new XmlParser().ParseDocument(content)
            : new HtmlParser().ParseDocument(content);

        List<IElement> rows = [.. document.QuerySelectorAll(CardigannTemplate.Apply(search.Rows.Selector, variables) ?? "")];

        // "after: n" glues the n following rows onto each row (titles and details on separate <tr>s).
        int after = search.Rows.After;
        for (int i = 0; after > 0 && i < rows.Count; i++)
        {
            int available = Math.Min(after, rows.Count - i - 1);
            foreach (IElement next in rows.GetRange(i + 1, available))
            {
                rows[i].Append([.. next.ChildNodes]);
            }

            rows.RemoveRange(i + 1, available);
        }

        List<ParsedRelease> releases = [];
        foreach (IElement row in rows)
        {
            try
            {
                ParsedRelease release = new();

                // A required field that fails is skipped like upstream (which only logs it); unusable rows are dropped later.
                foreach (SearchField field in search.Fields)
                {
                    ParseField(site, request, release, field, variables, required => HtmlSelect(site, field.Block, row, variables, required));
                }

                if (release.PublishDate == DateTime.MinValue && search.Rows.DateHeaders is { } dateHeaders)
                {
                    release.PublishDate = DateFromHeaders(site, dateHeaders, row, variables);
                }

                releases.Add(release);
            }
            catch (Exception)
            {
                // A row that cannot be read is skipped, whatever the definition got wrong (upstream logs it).
            }
        }

        return releases;
    }

    // Returns false when a required field failed.
    private static bool ParseField(CardigannSite site, CardigannRequest request, ParsedRelease release, SearchField field, Dictionary<string, object?> variables, Func<bool, string?> select)
    {
        string[] parts = field.Name.Split('|');
        string name = parts[0];
        string[] modifiers = parts[1..];
        string key = ".Result." + name;
        bool optional = OptionalFields.Contains(field.Name) || modifiers.Contains("optional") || field.Block.Optional;

        try
        {
            string? value = select(!optional);
            if (optional && string.IsNullOrWhiteSpace(value))
            {
                string? fallback = CardigannTemplate.Apply(field.Block.Default, variables);
                if (string.IsNullOrWhiteSpace(fallback))
                {
                    variables[key] = null;
                    return true;
                }

                value = fallback;
            }

            variables[key] = ApplyField(site, request.Url, release, name, modifiers, value ?? "");
            return true;
        }
        catch (Exception)
        {
            if (!variables.ContainsKey(key) || optional)
            {
                variables[key] = null;
            }

            return optional;
        }
    }

    // Upstream ParseFields: stores the field on the release and returns the value later templates see as .Result.<name>.
    private static string? ApplyField(CardigannSite site, Uri searchUrl, ParsedRelease release, string name, string[] modifiers, string value)
    {
        switch (name)
        {
            case "download":
                if (string.IsNullOrEmpty(value))
                {
                    release.DownloadUrl = null;
                    return null;
                }

                if (value.StartsWith("magnet:", StringComparison.Ordinal))
                {
                    release.MagnetUrl = value;
                }
                else
                {
                    release.DownloadUrl = site.ResolvePath(value, searchUrl).AbsoluteUri;
                    value = release.DownloadUrl;
                }

                release.Guid = value;
                return value;
            case "magnet":
                release.MagnetUrl = value;
                return value;
            case "infohash":
                release.InfoHash = value;
                return value;
            case "details":
                release.InfoUrl = site.ResolvePath(value, searchUrl).AbsoluteUri;
                return release.InfoUrl;
            case "comments" or "poster" when !string.IsNullOrWhiteSpace(value):
                return site.ResolvePath(value, searchUrl).AbsoluteUri;
            case "title":
                release.Title = modifiers.Contains("append") ? release.Title + value : value;
                return release.Title;
            case "description":
                release.Description = modifiers.Contains("append") ? release.Description + value : value;
                return release.Description;
            case "category":
                AddCategories(release, site.Categories.MapTrackerCategory(value), modifiers);
                return value;
            case "categorydesc":
                AddCategories(release, site.Categories.MapTrackerCategoryDescription(value), modifiers);
                return value;
            case "size":
                long size = ParseUtil.GetBytes(value);
                release.Size = size;
                return size.ToString(CultureInfo.InvariantCulture);
            case "leechers":
                // Absurd counts are scraping errors (upstream #6558).
                long leechers = ParseUtil.CoerceLong(value);
                leechers = leechers < 5000000L ? leechers : 0;
                release.Peers = (release.Peers ?? 0) + (int)leechers;
                return leechers.ToString(CultureInfo.InvariantCulture);
            case "seeders":
                int seeders = ParseUtil.CoerceInt(value);
                seeders = seeders < 5000000 ? seeders : 0;
                release.Seeders = seeders;
                release.Peers = (release.Peers ?? 0) + seeders;
                return seeders.ToString(CultureInfo.InvariantCulture);
            case "date":
                release.PublishDate = DateTimeUtil.FromUnknown(value);
                return DateTimeUtil.Format(release.PublishDate);
            case "grabs":
                int grabs = ParseUtil.CoerceInt(value);
                release.Grabs = grabs;
                return grabs.ToString(CultureInfo.InvariantCulture);
            case "downloadvolumefactor":
                double downloadFactor = ParseUtil.CoerceDouble(value);
                release.DownloadVolumeFactor = downloadFactor;
                return downloadFactor.ToString(CultureInfo.InvariantCulture);
            case "uploadvolumefactor":
                double uploadFactor = ParseUtil.CoerceDouble(value);
                release.UploadVolumeFactor = uploadFactor;
                return uploadFactor.ToString(CultureInfo.InvariantCulture);
            case "imdb" or "imdbid":
                release.ImdbId = Id(value);
                return IdText(release.ImdbId);
            case "tmdbid":
                release.TmdbId = Id(value);
                return IdText(release.TmdbId);
            case "tvdbid":
                release.TvdbId = Id(value);
                return IdText(release.TvdbId);
            default:
                return value;
        }
    }

    // Upstream stores 0 for "no id".
    private static int? Id(string value) => (int)ParseUtil.GetLongFromString(value).GetValueOrDefault() is int id and not 0 ? id : null;

    private static string IdText(int? id) => id.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);

    // "noappend" (deprecated) replaces earlier categories instead of adding to them.
    private static void AddCategories(ParsedRelease release, IEnumerable<int> categories, string[] modifiers)
    {
        List<int> mapped = [.. categories];
        if (mapped.Count > 0)
        {
            release.Categories = modifiers.Contains("noappend") ? mapped : [.. release.Categories.Union(mapped)];
        }
    }

    // Upstream HandleSelector: selector (or the row itself), remove, then case / attribute / text, then filters.
    private static string? HtmlSelect(CardigannSite site, SelectorBlock block, IElement row, Dictionary<string, object?> variables, bool required)
    {
        if (block.Text is not null)
        {
            return CardigannFilters.Apply(CardigannTemplate.Apply(block.Text, variables) ?? "", block.Filters, variables, site.Encoding);
        }

        IElement? selection = row;
        if (block.Selector is not null)
        {
            string selector = CardigannTemplate.Apply(block.Selector, variables) ?? "";
            selection = row.Matches(selector) ? row : QuerySelector(row, selector);
            if (selection is null)
            {
                return required ? throw new InvalidDataException($"Selector \"{selector}\" didn't match") : null;
            }
        }

        if (block.Remove is not null)
        {
            foreach (IElement removed in selection.QuerySelectorAll(block.Remove))
            {
                removed.Remove();
            }
        }

        string? value;
        if (block.Case is not null)
        {
            value = block.Case.FirstOrDefault(entry => selection.Matches(entry.Key) || QuerySelector(selection, entry.Key) is not null) is { Key: not null } match
                ? CardigannTemplate.Apply(match.Value, variables)
                : null;
            if (value is null)
            {
                return required ? throw new InvalidDataException($"None of the case selectors \"{string.Join(",", block.Case.Keys)}\" matched") : null;
            }
        }
        else if (block.Attribute is not null)
        {
            value = selection.GetAttribute(block.Attribute);
            if (value is null)
            {
                return required ? throw new InvalidDataException($"Attribute \"{block.Attribute}\" is not set") : null;
            }
        }
        else
        {
            value = selection.TextContent;
        }

        return CardigannFilters.Apply(value.Trim(), block.Filters, variables, site.Encoding);
    }

    // AngleSharp does not support :root, so it is resolved by hand.
    internal static IElement? QuerySelector(IElement element, string selector)
    {
        if (selector.StartsWith(":root", StringComparison.Ordinal))
        {
            selector = selector[5..];
            while (element.ParentElement is not null)
            {
                element = element.ParentElement;
            }
        }

        return element.QuerySelector(selector);
    }

    // Upstream HandleJsonSelector: a JSONPath-like selector with :has()/:not()/:contains() filters; arrays read as "a,b".
    private static string? JsonSelect(CardigannSite site, SelectorBlock block, JToken parent, Dictionary<string, object?> variables, bool required)
    {
        if (block.Text is not null)
        {
            return CardigannFilters.Apply(CardigannTemplate.Apply(block.Text, variables) ?? "", block.Filters, variables, site.Encoding);
        }

        string? value = null;
        if (block.Selector is not null)
        {
            string selector = CardigannTemplate.Apply(block.Selector.TrimStart('.'), variables) ?? "";
            string? path = JsonFieldSelector(parent, selector);
            JToken? selection = path is null ? null : parent.SelectToken(path);
            if (selection is null)
            {
                return required ? throw new InvalidDataException($"Selector \"{selector}\" didn't match JSON content") : null;
            }

            value = selection is JArray array ? string.Join(",", array) : selection.Value<string>();
        }

        if (block.Case is not null)
        {
            if (block.Case.FirstOrDefault(entry => entry.Key == value || entry.Key == "*") is { Key: not null } match)
            {
                value = CardigannTemplate.Apply(match.Value, variables);
            }

            if (value is null)
            {
                return required ? throw new InvalidDataException($"None of the case selectors \"{string.Join(",", block.Case.Keys)}\" matched JSON content") : null;
            }
        }

        return value is null ? null : CardigannFilters.Apply(value.Trim(), block.Filters, variables, site.Encoding);
    }

    // The rows array, keeping the rows that pass the selector's :has()/:not()/:contains() filters; null when absent.
    private static List<JToken>? JsonRows(JToken json, string rowSelector)
    {
        string selector = rowSelector.Split(':')[0];
        try
        {
            if (json.SelectToken(selector) is not JArray rows)
            {
                return null;
            }

            return [.. rows.Where(row => JsonFieldSelector(row.Value<JObject>()!, rowSelector[selector.Length..]) is not null)];
        }
        catch (Exception ex) when (ex is JsonException or InvalidCastException or ArgumentException)
        {
            return null;
        }
    }

    // The JSONPath part of the selector when the token passes its filters, else null.
    private static string? JsonFieldSelector(JToken json, string fieldSelector)
    {
        string selector = fieldSelector.Split(':')[0];
        JToken? token = string.IsNullOrWhiteSpace(selector) ? json : json.SelectToken(selector);
        if (token is null)
        {
            return null;
        }

        foreach (Match match in JsonFilterRegex().Matches(fieldSelector))
        {
            string key = match.Groups["key"].Value;
            bool passes = match.Groups["filter"].Value switch
            {
                "has" => (JsonFilterRegex().IsMatch(key) ? JsonFieldSelector(token, key) : token.SelectToken(key)?.ToString()) is not null,
                "not" => (JsonFilterRegex().IsMatch(key) ? JsonFieldSelector(token, key) : token.SelectToken(key)?.ToString()) is null,
                "contains" => token.ToString().Contains(key, StringComparison.Ordinal),
                // Upstream logs unknown filters and ignores them.
                _ => true,
            };

            if (!passes)
            {
                return null;
            }
        }

        return selector;
    }

    // dateheaders: the closest earlier row (or earlier sibling of the parent) that matches holds the date.
    private static DateTime DateFromHeaders(CardigannSite site, SelectorBlock dateHeaders, IElement row, Dictionary<string, object?> variables)
    {
        IElement? previous = row.PreviousElementSibling ?? row.ParentElement?.PreviousElementSibling;
        while (previous is not null)
        {
            try
            {
                if (HtmlSelect(site, dateHeaders, previous, variables, required: true) is { } value)
                {
                    return DateTimeUtil.FromUnknown(value);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or DomException)
            {
                // Not a header row: keep looking.
            }

            previous = previous.PreviousElementSibling ?? previous.ParentElement?.PreviousElementSibling;
        }

        return dateHeaders.Optional ? DateTime.MinValue : throw new InvalidDataException("No date header row found");
    }

    [GeneratedRegex(@"\:(?<filter>.+?)\((?<key>.+?)\)(?=:|\z)")]
    private static partial Regex JsonFilterRegex();
}

// The mutable release a row is parsed into (upstream TorrentInfo).
internal sealed class ParsedRelease
{
    public string? Guid { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? DownloadUrl { get; set; }
    public string? MagnetUrl { get; set; }
    public string? InfoHash { get; set; }
    public string? InfoUrl { get; set; }
    public long? Size { get; set; }
    public int? Seeders { get; set; }
    public int? Peers { get; set; }
    public int? Grabs { get; set; }
    public double? DownloadVolumeFactor { get; set; }
    public double? UploadVolumeFactor { get; set; }
    public DateTime PublishDate { get; set; } = DateTime.MinValue;
    public IReadOnlyList<int> Categories { get; set; } = [];
    public int? ImdbId { get; set; }
    public int? TmdbId { get; set; }
    public int? TvdbId { get; set; }
}
