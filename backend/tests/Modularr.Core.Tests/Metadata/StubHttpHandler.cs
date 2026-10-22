using System.Net;

namespace Modularr.Core.Tests.Metadata;

// Answers from recorded fixtures by URL prefix (first match wins); anything else is a 404. Records every request.
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly List<(string Prefix, HttpStatusCode Status, string Body)> _routes = [];

    public List<string> Requests { get; } = [];

    public List<string> UserAgents { get; } = [];

    public StubHttpHandler Fixture(string prefix, string fixtureName) =>
        Respond(prefix, HttpStatusCode.OK, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", fixtureName)));

    public StubHttpHandler Respond(string prefix, HttpStatusCode status, string body = "")
    {
        _routes.Add((prefix, status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.OriginalString;
        Requests.Add(url);
        UserAgents.Add(request.Headers.UserAgent.ToString());

        (string _, HttpStatusCode status, string body) = _routes.Find(r => url.StartsWith(r.Prefix, StringComparison.Ordinal)) is { Prefix: not null } route
            ? route
            : ("", HttpStatusCode.NotFound, "");

        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
