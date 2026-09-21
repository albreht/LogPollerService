using LogPollerService.Sources;

namespace LogPollerService;

public class LogPollerWorker : BackgroundService
{
    private readonly IEnumerable<ILogSource> _sources;
    private readonly ILogger<LogPollerWorker> _logger;
    private readonly string _stateDirectory;

    public LogPollerWorker(
        IEnumerable<ILogSource> sources,
        ILogger<LogPollerWorker> logger,
        IConfiguration configuration)
    {
        _sources = sources;
        _logger = logger;
        _stateDirectory = configuration.GetValue<string>("Poller:StateDirectory")
                           ?? @"state\";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sourcesList = _sources.ToList();

        if (sourcesList.Count == 0)
        {
            _logger.LogWarning("Brak skonfigurowanych Ÿróde³ logów (sekcja Poller:Sources) - us³uga nic nie robi.");
            return;
        }

        _logger.LogInformation(
            "Start us³ugi AppLogPollerService. Uruchamiam {Count} niezale¿nych zadañ odpytywania: {Sources}",
            sourcesList.Count, string.Join(", ", sourcesList.Select(s => s.Name)));

     
        var tasks = sourcesList
            .Select(source =>
            {
                var cursorStore = new CursorStore(Path.Combine(_stateDirectory, $"{Sanitize(source.Name)}.state"));
                var runner = new LogSourceRunner(source, cursorStore, _logger);

                
                return Task.Run(() => runner.RunAsync(stoppingToken), stoppingToken);
            })
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
