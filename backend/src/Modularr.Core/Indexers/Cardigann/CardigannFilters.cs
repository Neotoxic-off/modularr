using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Modularr.Core.Indexers.Definitions;
using Newtonsoft.Json.Linq;

namespace Modularr.Core.Indexers.Cardigann;

// Prowlarr CardigannBase.ApplyFilters: the value pipeline of a selector, keywordsfilters and preprocessingfilters.
public static class CardigannFilters
{
    private static readonly char[] ValidateDelimiters = [',', ' ', '/', ')', '(', '.', ';', '[', ']', '"', '|', ':'];

    // Windows' invalid file name characters on every OS, so "validfilename" gives the same title everywhere.
    private static readonly SearchValues<char> InvalidFileNameChars = SearchValues.Create("\"<>|:*?\\/" + new string([.. Enumerable.Range(0, 32).Select(code => (char)code)]));

    /// <exception cref="FormatException">A filter got a value it cannot convert (e.g. timeago with an unknown unit).</exception>
    public static string Apply(string data, IReadOnlyList<FilterBlock>? filters, IReadOnlyDictionary<string, object?> variables, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(data);
        foreach (FilterBlock filter in filters ?? [])
        {
            data = filter.Name switch
            {
                "querystring" => ParseUtil.GetArgumentFromQueryString(data, Arg(filter)) ?? "",
                "timeparse" or "dateparse" => DateParse(data, Arg(filter)),
                "regexp" => Regex.Match(data, Arg(filter) ?? "").Groups[1].Value,
                "re_replace" => Regex.Replace(data, Arg(filter, 0) ?? "", Template(Arg(filter, 1), variables)),
                "split" => Split(data, Arg(filter, 0) ?? "", int.Parse(Arg(filter, 1) ?? "0", CultureInfo.InvariantCulture)),
                "replace" => data.Replace(Arg(filter, 0) ?? "", Template(Arg(filter, 1), variables), StringComparison.Ordinal),
                "trim" => Arg(filter) is { Length: > 0 } cutset ? data.Trim(cutset[0]) : data.Trim(),
                "prepend" => Template(Arg(filter), variables) + data,
                "append" => data + Template(Arg(filter), variables),
                "tolower" => data.ToLower(CultureInfo.CurrentCulture),
                "toupper" => data.ToUpper(CultureInfo.CurrentCulture),
                "base64decode" => Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(data)),
                "base64encode" => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(data)),
                "urldecode" => UrlDecode(data, encoding),
                "urlencode" => UrlEncode(data, encoding),
                "htmldecode" => WebUtility.HtmlDecode(data),
                "htmlencode" => WebUtility.HtmlEncode(data),
                "timeago" or "reltime" => DateTimeUtil.Format(DateTimeUtil.FromTimeAgo(data)),
                "fuzzytime" => DateTimeUtil.Format(DateTimeUtil.FromUnknown(data)),
                "validfilename" => ValidFileName(data),
                "diacritics" => Arg(filter) == "replace" ? RemoveDiacritics(data) : throw new NotSupportedException("unsupported diacritics filter argument"),
                "jsonjoinarray" => string.Join(Arg(filter, 1), JObject.Parse(data).SelectToken(Arg(filter, 0) ?? "")?.Select(token => token.ToString()) ?? []),
                // Debug-only upstream (they log the value); there is no logger here, so they pass the value through.
                "hexdump" or "strdump" => data,
                "validate" => Validate(data, Arg(filter) ?? ""),
                // Upstream logs unknown filters and keeps the value.
                _ => data,
            };
        }

        return data;
    }

    // A scalar argument, or one item of a list argument.
    private static string? Arg(FilterBlock filter, int index = -1) => (index, filter.Args) switch
    {
        (-1, string scalar) => scalar,
        (-1, IList { Count: > 0 } list) => list[0]?.ToString(),
        (>= 0, IList list) when index < list.Count => list[index]?.ToString(),
        _ => null,
    };

    private static string Template(string? text, IReadOnlyDictionary<string, object?> variables) => CardigannTemplate.Apply(text, variables) ?? "";

    // An unparseable date is left as is for the date field to try (upstream logs it).
    private static string DateParse(string data, string? layout)
    {
        try
        {
            return DateTimeUtil.Format(DateTimeUtil.ParseDateTimeGoLang(data, layout ?? ""));
        }
        catch (FormatException)
        {
            return data;
        }
    }

    private static string Split(string data, string separator, int position)
    {
        string[] parts = data.Split(separator[0]);
        return parts[position < 0 ? position + parts.Length : position];
    }

    public static string UrlEncode(string? value, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        byte[] bytes = encoding.GetBytes(value);
        return encoding.GetString(WebUtility.UrlEncodeToBytes(bytes, 0, bytes.Length));
    }

    private static string UrlDecode(string value, Encoding encoding)
    {
        if (value.Length == 0)
        {
            return "";
        }

        byte[] bytes = encoding.GetBytes(value);
        return encoding.GetString(WebUtility.UrlDecodeToBytes(bytes, 0, bytes.Length));
    }

    private static string ValidFileName(string data) =>
        string.Create(data.Length, data, (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                span[i] = InvalidFileNameChars.Contains(source[i]) ? '_' : source[i];
            }
        });

    // Strips combining marks ("é" -> "e"); letters without a decomposition (đ) stay.
    private static string RemoveDiacritics(string data) =>
        new string([.. data.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)])
            .Normalize(NormalizationForm.FormC);

    // Keeps the words of the value that are in the allowed list.
    private static string Validate(string data, string allowed)
    {
        StringSplitOptions options = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;
        IEnumerable<string> words = data.ToLowerInvariant().Split(ValidateDelimiters, options);
        return string.Join(", ", allowed.ToLowerInvariant().Split(ValidateDelimiters, options).Intersect(words));
    }
}
