using System.Collections;

namespace TableTool.Runtime;

/// <summary>Base class for typed data tables with Dictionary-based O(1) lookup.</summary>
/// <typeparam name="TKey">Primary key type.</typeparam>
/// <typeparam name="TRecord">Record type.</typeparam>
public abstract class DataTable<TKey, TRecord> : IDataTable<TKey, TRecord>, IEnumerable<TRecord>
    where TKey : notnull
    where TRecord : class
{
    private Dictionary<TKey, TRecord> _records = new();
    private TKey[] _allKeys = Array.Empty<TKey>();

    /// <summary>Get a record by primary key. Throws KeyNotFoundException if missing.</summary>
    public TRecord Get(TKey key) =>
        _records.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException($"Key '{key}' not found in {GetType().Name}.");

    /// <summary>Try to get a record. Returns null if missing.</summary>
    public TRecord? TryGet(TKey key) =>
        _records.TryGetValue(key, out var v) ? v : null;

    /// <summary>Check if a key exists.</summary>
    public bool ContainsKey(TKey key) => _records.ContainsKey(key);

    /// <summary>Get all records.</summary>
    public IReadOnlyCollection<TRecord> GetAll() => _records.Values;

    /// <summary>Get all keys.</summary>
    public IReadOnlyCollection<TKey> GetAllKeys() => _allKeys;

    /// <summary>Number of records.</summary>
    public int Count => _records.Count;

    /// <summary>Enumerate all records.</summary>
    public IEnumerator<TRecord> GetEnumerator() => _records.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _records.Values.GetEnumerator();

    /// <summary>Internal method to load/replace data. Called by DataLoader via reflection.</summary>
    internal void Load(Dictionary<TKey, TRecord> data)
    {
        _records = data ?? new Dictionary<TKey, TRecord>();
        _allKeys = _records.Keys.ToArray();
    }
}
