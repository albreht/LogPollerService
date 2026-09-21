using LogPollerService.Sources;

namespace LogPollerService;


public class LogSourceRunner
{
    private readonly ILogSource _source;
    private readonly CursorStore _cursorStore;
    private readonly ILogger _serviceLogger;


    private readonly NLog.Logger _appLogWriter = NLog.LogManager.GetLogger("AppLogFile");

    public LogSourceRunner(ILogSource source, CursorStore cursorStore, ILogger serviceLogger)
    {
        _source = source;
        _cursorStore = cursorStore;
        _serviceLogger = serviceLogger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var cursor = _cursorStore.Load();
        _serviceLogger.LogInformation(
            "Start zadania odpytywania ürÛd≥a {Source}. Ostatni zapisany kursor = {Cursor}",
            _source.Name, cursor ?? "(brak - pierwsze uruchomienie)");

        while (!stoppingToken.IsCancellationRequested)
        {
            bool gotAnyLogs;

            try
            {
                var result = await _source.PollLogsAsync(cursor, stoppingToken);
                gotAnyLogs = result.Logs.Count > 0;

                if (gotAnyLogs)
                {
                    foreach (var logEvent in result.Logs)
                    {
                    
                        logEvent.Properties["SourceName"] = _source.Name;
                        _appLogWriter.Log(logEvent);
                    }

               
                    if (!string.IsNullOrEmpty(result.LastCursor))
                    {
                        cursor = result.LastCursor;
                        _cursorStore.Save(cursor);
                    }

                    _serviceLogger.LogInformation(
                        "èrÛd≥o {Source}: przetworzono {Count} logÛw. Nowy kursor = {Cursor}",
                        _source.Name, result.Logs.Count, cursor);
                }
                else
                {
                    _serviceLogger.LogDebug("èrÛd≥o {Source}: brak nowych logÛw.", _source.Name);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
               
                break;
            }
            catch (Exception ex)
            {
               
                _serviceLogger.LogError(ex, "B≥πd podczas odpytywania ürÛd≥a {Source}.", _source.Name);
                gotAnyLogs = false;
            }

            if (gotAnyLogs)
            {

                continue;
            }

            try
            {
    
                await Task.Delay(_source.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _serviceLogger.LogInformation("Zadanie odpytywania ürÛd≥a {Source} zakoÒczone.", _source.Name);
    }
}
