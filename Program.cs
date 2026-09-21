using LogPollerService;
using LogPollerService.Sources;
using NLog.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Rejestracja jako usługa Windows
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AppLogPollerService";
});

// Podpięcie NLog jako providera logowania
builder.Logging.ClearProviders();
builder.Logging.AddNLog();

// Wczytanie konfiguracji wielu źródeł typu "tabela SQL" z appsettings.json (Poller:Sources).
// Każdy wpis staje się osobną instancją ILogSource i dostanie własny, niezależny task pollingowy
// (patrz LogPollerWorker). Aby dodać nowy TYP źródła (np. plik, kolejka, REST API), wystarczy
// napisać kolejną implementację ILogSource i zarejestrować ją analogicznie poniżej -
// reszta systemu (CursorStore, LogSourceRunner, LogPollerWorker) nie wymaga żadnych zmian.
var sqlSources = builder.Configuration.GetSection("Poller:Sources").Get<List<SqlLogSourceOptions>>() ?? [];

foreach (var sourceOptions in sqlSources)
{
    builder.Services.AddSingleton<ILogSource>(sp =>
        new SqlTableLogSource(sourceOptions, sp.GetRequiredService<ILogger<SqlTableLogSource>>()));
}

builder.Services.AddHostedService<LogPollerWorker>();

var host = builder.Build();
host.Run();
