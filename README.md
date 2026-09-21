# AppLogPollerService

Windows Service (.NET 10) który cyklicznie odpytuje jedno lub wiele źródeł logów (domyślnie:
tabele SQL), zapisuje pobrane wiersze przez NLog do plików płaskich oraz zapamiętuje ostatnio
przetworzony "kursor" dla każdego źródła osobno, żeby restart usługi nie powodował ponownego
przetwarzania od początku ani skanowania całej tabeli.

## Architektura

```
ILogSource (interfejs)              <-- abstrakcja "pobierz logi z dowolnego miejsca"
  └─ SqlTableLogSource              <-- konkretna implementacja: tabela SQL
                                        (można dopisać kolejne: plik, kolejka, REST API, Event Log...)

LogSourceRunner                     <-- pętla pollingowa DLA JEDNEGO źródła
                                        (drenowanie paczek / czekanie na interwał / zapis kursora)

CursorStore                         <-- trwały, atomowy zapis kursora do pliku płaskiego (per źródło)

LogPollerWorker : BackgroundService <-- orkiestrator: dla każdego ILogSource tworzy
                                        LogSourceRunner i uruchamia go jako OSOBNY Task (Task.Run)
```

### `ILogSource` - abstrakcja źródła logów

```csharp
public sealed record LogPollResult(IReadOnlyList<NLog.LogEventInfo> Logs, string? LastCursor);

public interface ILogSource
{
    string Name { get; }
    TimeSpan PollInterval { get; }

    Task<LogPollResult> PollLogsAsync(string? lastCursor, CancellationToken cancellationToken);
}
```

- Metoda zwraca **jeden** obiekt `LogPollResult`, agregujący całą paczkę: listę `NLog.LogEventInfo`
  oraz `LastCursor` — jedną, wspólną wartość kursora wskazującą na ostatni pobrany rekord w tej
  paczce (np. najwyższe `Id`). Kursor nie jest już rozbity na poszczególne wpisy ani chowany
  w `Properties` — to jedno, jawne pole na poziomie całego wyniku.
- Kursor to zwykły `string`, więc pasuje zarówno do rosnącego `Id` z SQL, jak i do offsetu pliku,
  znacznika czasu, offsetu w kolejce itd. — w zależności od implementacji.
- `LastCursor == null` oznacza "paczka pusta, kursor bez zmian" (patrz `LogPollResult.Empty`).

**Czy źródło powinno samo pamiętać ostatnio pobrany rekord?** Świadomie: nie. `ILogSource` jest
celowo bezstanowe względem kursora — dostaje go jako parametr wejściowy i zwraca nowy razem z
wynikami, ale nie przechowuje go samodzielnie. Powody:
- **Jedna odpowiedzialność** — zadaniem źródła jest "pobierz dane z X mając punkt startowy",
  a nie zarządzanie plikami stanu i ich odpornością na awarie zapisu (to robi `CursorStore`).
- **Testowalność** — bezstanowe źródło to czysta funkcja (kursor wejściowy → wynik),
  łatwa do przetestowania jednostkowo bez dotykania dysku.
- **Wymienność persystencji** — zmiana sposobu zapisu kursora (plik → np. tabela) wymaga zmiany
  tylko w `CursorStore`, źródła pozostają nietknięte.
- **Jeden właściciel cyklu życia kursora** — `LogSourceRunner` jest jedynym miejscem, które
  decyduje kiedy i jak zapisać stan, więc nie ma ryzyka rozjazdu logiki między implementacjami źródeł.


### Każde źródło = osobny, niezależny task

`LogPollerWorker.ExecuteAsync` dla każdego zarejestrowanego `ILogSource`:
1. tworzy dedykowany `CursorStore` (osobny plik stanu, nazwany wg `source.Name`),
2. tworzy `LogSourceRunner` spinający źródło z jego magazynem kursora,
3. odpala `Task.Run(() => runner.RunAsync(stoppingToken), stoppingToken)`.

Wszystkie taski są uruchamiane równolegle i niezależnie (`Task.WhenAll`) — błąd, spowolnienie
albo zablokowanie jednego źródła nie wpływa na pozostałe. Dzięki `Task.Run` każde źródło startuje
na wątku z puli wątków, a sam polling wewnątrz jest asynchroniczny (await na I/O do bazy).

### Logika interwału (per źródło, niezależnie)

Zgodnie z wcześniejszymi ustaleniami: jeśli dana paczka zwróci choć jeden log, zakładamy że
mogą być kolejne zaległe rekordy i **od razu** wykonujemy kolejne odpytanie tego źródła — bez
czekania na `PollIntervalSeconds`. Dopiero pusta paczka (albo błąd) powoduje odczekanie pełnego
interwału właściwego dla danego źródła przed kolejną próbą.

### Brak table scanu (dla `SqlTableLogSource`)

Zapytanie ma postać `SELECT TOP (@BatchSize) * FROM Tabela WHERE Id > @LastId ORDER BY Id ASC`.
Przy PK / indeksie na kolumnie `Id` silnik bazy wykona to jako **index seek**, niezależnie od
rozmiaru tabeli.

### Odporność na restart

Po każdej udanej paczce `LogSourceRunner` zapisuje nowy kursor przez `CursorStore` do osobnego
pliku per źródło (`{StateDirectory}\{NazwaŹródła}.state`). Zapis jest atomowy (plik tymczasowy +
`File.Replace`/`File.Move`), więc nagłe zamknięcie procesu w trakcie zapisu nie uszkodzi pliku
stanu. Przy starcie każdy runner wczytuje swój kursor i kontynuuje dokładnie od tego miejsca.

## Konfiguracja wielu źródeł (appsettings.json)

```json
"Poller": {
  "StateDirectory": "C:\\ProgramData\\AppLogPollerService\\state",
  "Sources": [
    {
      "Name": "AppLogPrimary",
      "ConnectionString": "Server=localhost;Database=AppDb;...",
      "TableName": "dbo.AppLog",
      "IdColumn": "Id",
      "PollIntervalSeconds": 5,
      "BatchSize": 500
    },
    {
      "Name": "AppLogArchive",
      "ConnectionString": "Server=archive-server;Database=ArchiveDb;...",
      "TableName": "dbo.OldAppLog",
      "IdColumn": "LogId",
      "PollIntervalSeconds": 30,
      "BatchSize": 200
    }
  ]
}
```

Każdy wpis w `Sources` to osobna tabela/baza, osobny plik stanu i osobny plik wynikowy z logami
(`applog_{Name}_{data}.log`) — dzięki właściwości `SourceName` doklejanej automatycznie w
`LogSourceRunner` i wykorzystywanej w `nlog.config`.

## Dodawanie nowego TYPU źródła (nie tylko SQL)

1. Utwórz klasę implementującą `ILogSource` (np. `RestApiLogSource`, `FileTailLogSource`,
   `EventLogSource`) — jedyny wymóg to zwrócenie `LogPollResult` (lista logów + jeden wspólny
   kursor ostatniego rekordu, albo `LogPollResult.Empty`, gdy nie ma nic nowego).
2. Zarejestruj instancję/instancje w `Program.cs`:
   ```csharp
   builder.Services.AddSingleton<ILogSource>(sp => new RestApiLogSource(...));
   ```
3. Nic więcej nie trzeba zmieniać — `LogPollerWorker` automatycznie odpali dla niej osobny task,
   z własnym plikiem stanu i własną logiką drenowania/czekania.

## Dostosowanie mapowania kolumn (SqlTableLogSource)

W `Sources/SqlTableLogSource.cs`, w metodzie `PollLogsAsync`, dostosuj nazwy kolumn
(`LogDate`, `Level`, `Logger`, `Message`, `Exception`) do rzeczywistej struktury Twojej tabeli.

## Publikacja

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o C:\Deploy\AppLogPollerService
```

## Instalacja jako usługa Windows

Uruchom PowerShell/CMD jako Administrator:

```powershell
sc.exe create AppLogPollerService binPath= "C:\Deploy\AppLogPollerService\LogPollerService.exe" start= auto
sc.exe description AppLogPollerService "Cyklicznie odpytuje skonfigurowane źródła logów i zapisuje do plików przez NLog."
sc.exe start AppLogPollerService
```

Uwaga: w `binPath=` musi być spacja po znaku `=` (wymóg `sc.exe`).

## Deinstalacja

```powershell
sc.exe stop AppLogPollerService
sc.exe delete AppLogPollerService
```

## Wymagane katalogi / uprawnienia

Konto, na którym działa usługa, musi mieć prawo zapisu do:
- `C:\Logs\AppLogPollerService\` (logi NLog, osobne pliki per źródło),
- `C:\ProgramData\AppLogPollerService\state\` (pliki stanu z kursorami, osobne per źródło).

## Pakiety NuGet użyte w projekcie

- `Microsoft.Extensions.Hosting.WindowsServices` — integracja z Windows Service Control Manager.
- `Microsoft.Data.SqlClient` — dostęp do SQL Server (implementacja `SqlTableLogSource`).
- `NLog.Extensions.Logging` — NLog jako provider `Microsoft.Extensions.Logging` + zapis do plików płaskich.
