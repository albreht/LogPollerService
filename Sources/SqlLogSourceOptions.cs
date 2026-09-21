namespace LogPollerService.Sources;

/// <summary>
/// Konfiguracja pojedynczego źródła typu "tabela SQL" (jeden wpis w Poller:Sources w appsettings.json).
/// Można skonfigurować wiele takich źródeł jednocześnie - każde dostanie własny task pollingowy.
/// </summary>
public class SqlLogSourceOptions
{
    /// <summary>Unikalna nazwa źródła, np. "AppLogPrimary", "AppLogArchive". Używana w nazwach plików.</summary>
    public string Name { get; set; } = "Default";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Pełna nazwa tabeli, np. "dbo.AppLog".</summary>
    public string TableName { get; set; } = "dbo.AppLog";

    /// <summary>Nazwa kolumny z rosnącym, unikalnym identyfikatorem (najlepiej klucz główny / indeks klastrowany).</summary>
    public string IdColumn { get; set; } = "Id";

    /// <summary>Co ile sekund odpytywać, gdy w poprzedniej paczce nie było nowych wierszy.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Maksymalna liczba wierszy pobierana w jednym cyklu (TOP N).</summary>
    public int BatchSize { get; set; } = 500;
}
