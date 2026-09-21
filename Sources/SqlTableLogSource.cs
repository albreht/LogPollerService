using Microsoft.Data.SqlClient;

namespace LogPollerService.Sources;


public class SqlTableLogSource : ILogSource
{
    private readonly SqlLogSourceOptions _options;
    private readonly ILogger<SqlTableLogSource> _logger;

    public SqlTableLogSource(SqlLogSourceOptions options, ILogger<SqlTableLogSource> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string Name => _options.Name;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(_options.PollIntervalSeconds);

    public async Task<LogPollResult> PollLogsAsync(string? lastCursor, CancellationToken cancellationToken)
    {
        var lastId = ParseCursor(lastCursor);

        var query = $@"
SELECT TOP (@BatchSize) *
FROM {_options.TableName}
WHERE {_options.IdColumn} > @LastId
ORDER BY {_options.IdColumn} ASC;";

        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(query, connection);
        command.Parameters.Add("@BatchSize", System.Data.SqlDbType.Int).Value = _options.BatchSize;
        command.Parameters.Add("@LastId", System.Data.SqlDbType.BigInt).Value = lastId;

        var logs = new List<NLog.LogEventInfo>();
        long maxIdInBatch = lastId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var idOrdinal = -1;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (idOrdinal < 0)
            {
                idOrdinal = reader.GetOrdinal(_options.IdColumn);
            }

            var id = reader.GetInt64(idOrdinal);

            // UWAGA: dostosuj nazwy kolumn (LogDate, Level, Logger, Message, Exception)
            // do rzeczywistej struktury Twojej tabeli logów.
            var logDate = SafeGet<DateTime?>(reader, "LogDate") ?? DateTime.Now;
            var level = SafeGet<string?>(reader, "Level");
            var loggerName = SafeGet<string?>(reader, "Logger") ?? "App";
            var message = SafeGet<string?>(reader, "Message") ?? string.Empty;
            var exception = SafeGet<string?>(reader, "Exception");

            var logEvent = new NLog.LogEventInfo
            {
                TimeStamp = logDate,
                LoggerName = loggerName,
                Message = message,
                Level = MapLevel(level)
            };

            if (!string.IsNullOrEmpty(exception))
            {
                logEvent.Properties["Exception"] = exception;
            }

            logs.Add(logEvent);

            if (id > maxIdInBatch)
            {
                maxIdInBatch = id;
            }
        }

        _logger.LogDebug(
            "ród³o {Source}: pobrano {Count} wierszy z tabeli {Table} (Id > {LastId}).",
            Name, logs.Count, _options.TableName, lastId);

        if (logs.Count == 0)
        {
            return LogPollResult.Empty;
        }

        // Jedna, wspólna wartoœæ kursora dla ca³ej paczki - najwy¿sze Id spoœród pobranych wierszy.
        return new LogPollResult(logs, maxIdInBatch.ToString());
    }

    private static long ParseCursor(string? cursor)
    {
        return !string.IsNullOrEmpty(cursor) && long.TryParse(cursor, out var value) ? value : 0L;
    }

    private static T SafeGet<T>(System.Data.Common.DbDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return default!;
        }

        return (T)reader.GetValue(ordinal);
    }

    private static NLog.LogLevel MapLevel(string? level)
    {
        return level?.Trim().ToUpperInvariant() switch
        {
            "TRACE" => NLog.LogLevel.Trace,
            "DEBUG" => NLog.LogLevel.Debug,
            "INFO" or "INFORMATION" => NLog.LogLevel.Info,
            "WARN" or "WARNING" => NLog.LogLevel.Warn,
            "ERROR" => NLog.LogLevel.Error,
            "FATAL" or "CRITICAL" => NLog.LogLevel.Fatal,
            _ => NLog.LogLevel.Info
        };
    }
}
