using Microsoft.Extensions.Logging;

namespace azureai.src;

internal static class LoggerProvider
{
    private static readonly ILoggerFactory Factory = LoggerFactory.Create(f => f.AddConsole());

    public static ILogger Logger => Factory.CreateLogger("Program");
}
