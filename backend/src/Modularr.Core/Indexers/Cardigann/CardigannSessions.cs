using System.Collections.Concurrent;

namespace Modularr.Core.Indexers.Cardigann;

// Session cookies of logged-in indexers, per indexer and host (mirrors log in separately); memory only, a restart logs in again.
public sealed class CardigannSessions
{
    private readonly ConcurrentDictionary<(string Id, string Host), ConcurrentDictionary<string, string>> _jars = new();

    internal ConcurrentDictionary<string, string> Jar(string indexerId, string host) =>
        _jars.GetOrAdd((indexerId.ToUpperInvariant(), host.ToUpperInvariant()), _ => new(StringComparer.Ordinal));

    // The Cookie header for a request to this host, or null without a session.
    public string? CookieHeader(string indexerId, Uri url)
    {
        ArgumentNullException.ThrowIfNull(indexerId);
        ArgumentNullException.ThrowIfNull(url);
        return _jars.TryGetValue((indexerId.ToUpperInvariant(), url.Host.ToUpperInvariant()), out ConcurrentDictionary<string, string>? jar) && !jar.IsEmpty
            ? Format(jar)
            : null;
    }

    // New settings: the next request logs in with them.
    public void Clear(string indexerId)
    {
        ArgumentNullException.ThrowIfNull(indexerId);
        foreach ((string Id, string Host) key in _jars.Keys.Where(key => key.Id.Equals(indexerId, StringComparison.OrdinalIgnoreCase)))
        {
            _jars.TryRemove(key, out _);
        }
    }

    internal static string Format(IEnumerable<KeyValuePair<string, string>> cookies) => string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}"));

    // Prowlarr CookieUtil.CookieHeaderToDictionary: "a=1; b=2" (a value may itself contain '=').
    internal static void AddHeader(ConcurrentDictionary<string, string> jar, string? header)
    {
        foreach (string[] pair in (header ?? "").Split(';').Select(part => part.Trim().Split('=', 2)).Where(pair => pair.Length == 2 && pair[0].Length > 0))
        {
            jar[pair[0]] = pair[1];
        }
    }
}
