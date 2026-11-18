using System.Text.Json;

namespace Trading.Core;

/// <summary>
/// Crash-tolerant journal of JSON documents, one file per id, used to replay
/// host state after an interruption (backtest jobs, kill-switch state, …).
///
/// Durability model: every state transition is written through as a whole
/// document via temp-file + atomic rename, so a crash mid-write can never
/// corrupt the previous version; a document that still fails to parse on
/// load is skipped (and reported), never fatal. This favors simplicity over
/// write throughput — callers journal lifecycle transitions, not ticks.
/// </summary>
public sealed class JsonDocumentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _dir;
    private readonly object _gate = new();

    public JsonDocumentStore(string dir) => _dir = Path.GetFullPath(dir);

    public string Directory => _dir;

    /// <summary>Write through one document (temp file + atomic rename).</summary>
    public void Save<T>(string id, T document)
    {
        var path = PathFor(id);
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(_dir);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            var path = PathFor(id);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// Load every readable document. Unparseable files (torn by a crash
    /// mid-write before the store existed, or hand-edited) are reported via
    /// <paramref name="onSkipped"/> and left on disk for inspection.
    /// </summary>
    public IReadOnlyList<T> LoadAll<T>(Action<string, Exception>? onSkipped = null)
    {
        var documents = new List<T>();
        lock (_gate)
        {
            if (!System.IO.Directory.Exists(_dir))
                return documents;
            foreach (var path in System.IO.Directory.EnumerateFiles(_dir, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) is { } document)
                        documents.Add(document);
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    onSkipped?.Invoke(path, ex);
                }
            }
        }
        return documents;
    }

    /// <summary>
    /// File name = the id itself; ids come from our own generators (hex guids,
    /// account keys), so only path separators and drive-qualifier characters
    /// need replacing to stay a single flat file per id on every OS.
    /// </summary>
    private string PathFor(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("document id must be non-empty", nameof(id));
        var safe = string.Join('_', id.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_dir, $"{safe}.json");
    }
}

/// <summary>
/// A single JSON state file with the same durability model as
/// <see cref="JsonDocumentStore"/> (atomic replace, tolerant load) for
/// small whole-map state like kill switches or terminal subscriptions.
/// </summary>
public sealed class JsonStateFile<T> where T : class, new()
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonStateFile(string path) => _path = Path.GetFullPath(path);

    /// <summary>The persisted state, or a fresh <typeparamref name="T"/> when
    /// the file is missing or unreadable (reported via <paramref name="onError"/>).</summary>
    public T Load(Action<Exception>? onError = null)
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) &&
                    JsonSerializer.Deserialize<T>(File.ReadAllText(_path), JsonOptions) is { } state)
                    return state;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                onError?.Invoke(ex);
            }
            return new T();
        }
    }

    public void Save(T state)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
    }

    /// <summary>Load, mutate, save — one writer at a time.</summary>
    public T Update(Action<T> mutate)
    {
        lock (_gate)
        {
            var state = Load();
            mutate(state);
            Save(state);
            return state;
        }
    }
}
