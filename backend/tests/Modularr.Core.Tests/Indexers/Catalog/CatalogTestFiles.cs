using System.IO.Compression;
using System.Net;

namespace Modularr.Core.Tests.Indexers.Catalog;

internal static class CatalogTestFiles
{
    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Indexers", "Definitions", "Fixtures", name));

    // A semi-private video tracker (no real fixture combines both).
    internal static readonly string SemiPrivateVideo = """
        id: semivideo
        name: Semi Video
        description: Semi-private video tracker
        language: fr-FR
        type: semi-private
        links:
          - https://semi.example/
        caps:
          categorymappings:
            - {id: 1, cat: Movies/HD, desc: "Films"}
          modes:
            search: [q]
        search:
          path: browse.php
          rows:
            selector: tr
          fields:
            title:
              selector: a
        """.ReplaceLineEndings("\n");

    internal static void Write(string directory, IReadOnlyDictionary<string, string> files)
    {
        Directory.CreateDirectory(directory);
        foreach ((string name, string content) in files)
        {
            File.WriteAllText(Path.Combine(directory, name), content);
        }
    }

    internal static byte[] Zip(IReadOnlyDictionary<string, string> files)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in files)
            {
                using StreamWriter writer = new(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}

// Serves canned responses so no test touches the network; records every requested URL.
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    public static StubHandler Serving(byte[] body) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

    public static StubHandler Offline() => new(_ => throw new HttpRequestException("No such host is known."));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
