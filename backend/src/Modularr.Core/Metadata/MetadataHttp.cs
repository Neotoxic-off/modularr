using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Modularr.Core.Metadata;

internal static class MetadataHttp
{
    // SkyHook answers 400 to requests without a User-Agent (upstream sends "Sonarr/<version>").
    private static readonly ProductInfoHeaderValue UserAgent = new("Modularr", typeof(MetadataHttp).Assembly.GetName().Version?.ToString() ?? "1.0");

    // Null on 404; every other failure becomes a MetadataSourceException. Caller cancellation propagates untouched.
    public static async Task<T?> GetJsonAsync<T>(HttpClient httpClient, string url, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(url));
            request.Headers.UserAgent.Add(UserAgent);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("Empty response body.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new MetadataSourceException($"Request to {url} failed: {ex.Message}", ex);
        }
    }

    // Base URLs come from config with or without the trailing slash.
    public static string Combine(string baseUrl, string relative) => $"{baseUrl.TrimEnd('/')}/{relative}";
}
