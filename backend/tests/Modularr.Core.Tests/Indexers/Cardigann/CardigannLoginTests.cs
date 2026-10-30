using System.Net;
using Modularr.Core.Configuration;
using Modularr.Core.Indexers;
using Modularr.Core.Indexers.Cardigann;
using Modularr.Core.Indexers.Catalog;
using Modularr.Core.Indexers.Definitions;
using static Modularr.Core.Tests.Indexers.Cardigann.CardigannFixtures;

namespace Modularr.Core.Tests.Indexers.Cardigann;

// Private trackers: the login block (Prowlarr DoLogin) runs before searching, and once more when the session is gone.
public sealed class CardigannLoginTests
{
    private static readonly CardigannSearchQuery Query = new() { Term = "ubuntu" };

    // Shaped like Prowlarr's private Gazelle/TBDev definitions: a login form, an error box and a logout link when logged in.
    private const string FormDefinition = """
        id: privatesite
        name: PrivateSite
        description: A private tracker
        language: en-US
        type: private
        links:
          - https://private.example/
        caps:
          categorymappings:
            - {id: 1, cat: Movies, desc: "Movies"}
          modes:
            search: [q]
        settings:
          - name: username
            type: text
            label: Username
          - name: password
            type: password
            label: Password
          - name: freeleech
            type: checkbox
            label: Search freeleech only
            default: false
          - name: info_tpp
            type: info
            label: Results Per Page
            default: For best results, change the <b>Torrents per page:</b> setting to <b>100</b> on your account profile.
        login:
          path: login.php
          method: form
          form: form#login
          inputs:
            username: "{{ .Config.username }}"
            password: "{{ .Config.password }}"
            keeplogged: 1
          error:
            - selector: div.error
          test:
            path: index.php
            selector: a[href*="logout.php"]
        search:
          paths:
            - path: browse.php
          inputs:
            search: "{{ .Keywords }}"
            freeleech: "{{ if .Config.freeleech }}1{{ else }}{{ end }}"
          rows:
            selector: tr.torrent
          fields:
            title:
              selector: a.title
            download:
              selector: a.dl
              attribute: href
        """;

    private const string CookieDefinition = """
        id: cookiesite
        name: CookieSite
        description: A private tracker behind a captcha
        language: en-US
        type: private
        links:
          - https://cookie.example/
        caps:
          categorymappings:
            - {id: 1, cat: TV, desc: "TV"}
          modes:
            search: [q]
        settings:
          - name: cookie
            type: text
            label: Cookie
          - name: info_cookie
            type: info_cookie
            label: About Cookie
        login:
          method: cookie
          inputs:
            cookie: "{{ .Config.cookie }}"
          test:
            path: index.php
            selector: a[href*="logout"]
        search:
          paths:
            - path: browse.php
          inputs:
            q: "{{ .Keywords }}"
          rows:
            selector: tr.torrent
          fields:
            title:
              selector: a.title
            download:
              selector: a.dl
              attribute: href
        """;

    private const string Results = """<a href="logout.php">Logout</a><table><tr class="torrent"><td><a class="title">Ubuntu 24.04</a><a class="dl" href="/download.php?id=1"></a></td></tr></table>""";

    private static readonly Dictionary<string, string> Credentials = new() { ["username"] = "neo", ["password"] = "s3cr3t-pass" };

    private static IndexerOptions WithSettings(string id, Dictionary<string, string> values) =>
        new() { Settings = new Dictionary<string, IReadOnlyDictionary<string, string>> { [id] = values } };

    private static CardigannDefinition Load(string yaml) => CardigannDefinitionLoader.Load(yaml.ReplaceLineEndings("\n"));

    private static HttpResponseMessage Redirect(string location, string? setCookie = null)
    {
        HttpResponseMessage response = new(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };
        if (setCookie is not null)
        {
            response.Headers.Add("Set-Cookie", setCookie);
        }

        return response;
    }

    // A tracker that sets session=<n> on each good login and answers searches only with the current session cookie.
    private sealed class Tracker
    {
        public int Logins { get; private set; }
        public bool AcceptLogin { get; set; } = true;
        public string Session { get; set; } = "";

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            string path = request.RequestUri!.AbsolutePath;
            bool loggedIn = Session.Length > 0 && request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies) && cookies.Any(c => c.Contains($"session={Session}", StringComparison.Ordinal));
            switch (path)
            {
                case "/login.php":
                    return StubHttpHandler.Ok("""<form id="search"><input name="q"></form><form id="login" action="takelogin.php" method="post"><input type="hidden" name="token" value="abc"><input name="username"><input name="password" type="password"><input type="checkbox" name="remember" value="1"><input name="old" disabled value="x"></form>""");
                case "/takelogin.php" when !AcceptLogin:
                    return StubHttpHandler.Ok("""<div class="error">Invalid password s3cr3t-pass for neo</div>""");
                case "/takelogin.php":
                    Logins++;
                    Session = $"s{Logins}";
                    return Redirect("/index.php", $"session={Session}; path=/; HttpOnly");
                case "/index.php" or "/browse.php" when !loggedIn:
                    return Redirect("/login.php");
                case "/index.php":
                    return StubHttpHandler.Ok("""<a href="logout.php">Logout</a>""");
                default:
                    return StubHttpHandler.Ok(Results);
            }
        }
    }

    [Fact]
    public async Task FormLoginPostsTheMergedInputsAndSearchesWithTheSessionCookie()
    {
        Tracker tracker = new();
        StubHttpHandler handler = new(tracker.Respond);

        CardigannSearchResult result = await Searcher(handler, WithSettings("privatesite", Credentials)).SearchAsync(Load(FormDefinition), null, Query, TestContext.Current.CancellationToken);

        Assert.Equal("Ubuntu 24.04", Assert.Single(Releases(result)).Title);
        RecordedRequest post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal("https://private.example/takelogin.php", post.Url.AbsoluteUri);
        Assert.Equal("token=abc&username=neo&password=s3cr3t-pass&keeplogged=1", post.Body);
        RecordedRequest search = Assert.Single(handler.Requests, r => r.Url.AbsolutePath == "/browse.php");
        Assert.Equal("session=s1", search.Headers["Cookie"]);
        Assert.Equal(1, tracker.Logins);
    }

    [Fact]
    public async Task ExpiredSessionLogsInOnceMoreAndRetries()
    {
        Tracker tracker = new();
        CardigannSessions sessions = new();
        CardigannSearcher searcher = Searcher(new StubHttpHandler(tracker.Respond), WithSettings("privatesite", Credentials), sessions);
        CardigannDefinition definition = Load(FormDefinition);
        Assert.Single(Releases(await searcher.SearchAsync(definition, null, Query, TestContext.Current.CancellationToken)));

        // The site killed the session: the search lands on the login page, so one fresh login runs before the retry.
        tracker.Session = "killed";
        Assert.Single(Releases(await searcher.SearchAsync(definition, null, Query, TestContext.Current.CancellationToken)));

        Assert.Equal(2, tracker.Logins);
        Assert.Equal("session=s2", sessions.CookieHeader("privatesite", new Uri("https://private.example/download.php?id=1")));
    }

    [Fact]
    public async Task LoginErrorSelectorIsAFailureWithoutTheSecret()
    {
        Tracker tracker = new() { AcceptLogin = false };

        CardigannFailure failure = Failure(await Searcher(new StubHttpHandler(tracker.Respond), WithSettings("privatesite", Credentials))
            .SearchAsync(Load(FormDefinition), null, Query, TestContext.Current.CancellationToken));

        Assert.Equal(CardigannFailureKind.LoginFailed, failure.Kind);
        Assert.Equal("privatesite: login failed: Invalid password *** for neo", failure.Message);
    }

    [Fact]
    public async Task CookieLoginSendsTheUserCookie()
    {
        static bool HasUserCookie(string? header) =>
            header?.Split("; ").Order(StringComparer.Ordinal).SequenceEqual(["pass=abcdef", "uid=7"]) == true;
        StubHttpHandler handler = new(request => HasUserCookie(request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies) ? cookies.Single() : null)
            ? StubHttpHandler.Ok(Results.Replace("logout.php", "logout", StringComparison.Ordinal))
            : Redirect("/login.php"));

        CardigannSearchResult result = await Searcher(handler, WithSettings("cookiesite", new() { ["cookie"] = "uid=7; pass=abcdef" }))
            .SearchAsync(Load(CookieDefinition), null, Query, TestContext.Current.CancellationToken);

        Assert.Single(Releases(result));
        Assert.All(handler.Requests, request => Assert.True(HasUserCookie(request.Headers["Cookie"])));
    }

    [Fact]
    public async Task ARejectedCookieFailsTheLoginTestWithoutEchoingIt()
    {
        StubHttpHandler handler = new(_ => Redirect("https://cookie.example/login.php?returnto=uid%3D7%3B%20pass%3Dabcdef"));

        CardigannFailure failure = Failure(await Searcher(handler, WithSettings("cookiesite", new() { ["cookie"] = "uid=7; pass=abcdef" }))
            .SearchAsync(Load(CookieDefinition), null, Query, TestContext.Current.CancellationToken));

        Assert.Equal(CardigannFailureKind.LoginFailed, failure.Kind);
        Assert.DoesNotContain("abcdef", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginSettingsDecideWhetherAnIndexerCanRun()
    {
        CardigannDefinition form = Load(FormDefinition);
        CardigannDefinition cookie = Load(CookieDefinition);

        // A private site whose templates read no credential runs like a public one.
        CardigannDefinition open = form with { Login = null, UsedSettings = new HashSet<string> { "freeleech" } };

        Assert.Equal(["username", "password"], CardigannSettings.Required(form).Select(f => f.Name));
        Assert.Equal(["cookie"], CardigannSettings.Required(cookie).Select(f => f.Name));
        Assert.False(CardigannSearcher.IsSupported(form));
        Assert.False(CardigannSearcher.IsSupported(form, new Dictionary<string, string> { ["username"] = "neo" }));
        Assert.True(CardigannSearcher.IsSupported(form, Credentials));
        Assert.False(CardigannSearcher.IsSupported(cookie));
        Assert.True(CardigannSearcher.IsSupported(open));
        Assert.True(CardigannSettings.IsInfo(form.Settings[3]));
        Assert.True(CardigannSettings.IsSecret(cookie.Settings[0]));
    }

    [Fact]
    public void MergeKeepsAStoredSecretWhenLeftEmptyAndRefusesUnknownFields()
    {
        CardigannDefinition form = Load(FormDefinition);

        IReadOnlyDictionary<string, string> merged = CardigannSettings.Merge(form, Credentials, new Dictionary<string, string?> { ["username"] = "trinity", ["password"] = "", ["freeleech"] = "true" });

        Assert.Equal(new Dictionary<string, string> { ["username"] = "trinity", ["password"] = "s3cr3t-pass", ["freeleech"] = "true" }, merged);
        Assert.Throws<ArgumentException>(() => CardigannSettings.Merge(form, null, new Dictionary<string, string?> { ["passkey"] = "x" }));
        Assert.Throws<ArgumentException>(() => CardigannSettings.Merge(form, null, new Dictionary<string, string?> { ["info_tpp"] = "x" }));
        Assert.Throws<ArgumentException>(() => CardigannSettings.Merge(form, null, new Dictionary<string, string?> { ["freeleech"] = "maybe" }));
    }

    [Fact]
    public void NonAdminsSeeNoIndexerSettings()
    {
        ModularrOptions options = new() { Indexers = WithSettings("privatesite", Credentials) };

        Assert.Empty(options.Redacted().Indexers.Settings);
        Assert.NotEmpty(options.Indexers.Settings);
    }

    [Fact]
    public void CatalogEnablesAPrivateIndexerOnceItsLoginIsFilledIn()
    {
        string directory = Path.Combine(Path.GetTempPath(), "modularr-login-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "privatesite.yml"), FormDefinition);

            Assert.False(Assert.Single(IndexerCatalog.Load(new IndexerOptions { DefinitionsDirectory = directory }).Entries).Enabled);
            Assert.True(Assert.Single(IndexerCatalog.Load(WithSettings("privatesite", Credentials) with { DefinitionsDirectory = directory }).Entries).Enabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
