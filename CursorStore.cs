namespace LogPollerService;


public class CursorStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public CursorStore(string path)
    {
        _path = path;

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Wczytuje ostatnio zapisany kursor. Zwraca null, jeœli plik jeszcze nie istnieje (pierwsze uruchomienie).</summary>
    public string? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var text = File.ReadAllText(_path).Trim();
            return text.Length == 0 ? null : text;
        }
    }


    public void Save(string cursor)
    {
        lock (_lock)
        {
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, cursor);

            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
    }
}
