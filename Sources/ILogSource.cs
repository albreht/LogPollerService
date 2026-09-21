namespace LogPollerService.Sources;


public sealed record LogPollResult(IReadOnlyList<NLog.LogEventInfo> Logs, string? LastCursor)
{
    public static readonly LogPollResult Empty = new(Array.Empty<NLog.LogEventInfo>(), null);
}


public interface ILogSource
{

    string Name { get; }

    
    TimeSpan PollInterval { get; }


    Task<LogPollResult> PollLogsAsync(string? lastCursor, CancellationToken cancellationToken);
}
