using Microsoft.Extensions.Logging;

namespace Modularr.Core.Configuration;

// Background jobs catch their failures so one bad cycle never stops the app; this keeps those failures visible in the log.
public static partial class BackgroundLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Job} failed; it runs again on its next cycle")]
    public static partial void JobFailed(this ILogger logger, string job, Exception exception);
}
