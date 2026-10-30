namespace Modularr.Core.Indexers.Cardigann;

public enum CardigannFailureKind
{
    // A login whose settings are not filled in yet, or a definition feature the engine does not port.
    Unsupported,

    // The site refused the login (error selector, login test, captcha) or kept asking for it.
    LoginFailed,

    // Cloudflare or DDoS-Guard served a challenge page (FlareSolverr would be needed).
    CloudflareProtected,

    Timeout,

    // Connection error or an unexpected status code.
    HttpError,

    // The site redirected a search that does not allow it (often a dead or moved domain).
    Redirected,

    // The definition cannot be evaluated (bad template or setting), or the response cannot be read as a whole.
    InvalidResponse,

    // The download block found no .torrent or magnet link.
    DownloadNotFound,
}

public sealed record CardigannFailure(CardigannFailureKind Kind, string Message);

public abstract record CardigannSearchResult
{
    private CardigannSearchResult()
    {
    }

    public sealed record Found(IReadOnlyList<ReleaseInfo> Releases) : CardigannSearchResult;

    public sealed record Failed(CardigannFailure Failure) : CardigannSearchResult;
}

public abstract record CardigannDownloadResult
{
    private CardigannDownloadResult()
    {
    }

    // A magnet URI or the URL of the .torrent file.
    public sealed record Resolved(Uri Link) : CardigannDownloadResult;

    public sealed record Failed(CardigannFailure Failure) : CardigannDownloadResult;
}
