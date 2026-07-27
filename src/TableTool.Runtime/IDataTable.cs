namespace TableTool.Runtime;

/// <summary>Generic interface for a typed data table.</summary>
/// <typeparam name="TKey">Primary key type.</typeparam>
/// <typeparam name="TRecord">Record type.</typeparam>
public interface IDataTable<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
{
    /// <summary>Get a record by primary key. Throws KeyNotFoundException if missing.</summary>
    TRecord Get(TKey key);

    /// <summary>Try to get a record. Returns null if missing.</summary>
    TRecord? TryGet(TKey key);

    /// <summary>Check if a key exists.</summary>
    bool ContainsKey(TKey key);

    /// <summary>Get all records.</summary>
    IReadOnlyCollection<TRecord> GetAll();

    /// <summary>Get all keys.</summary>
    IReadOnlyCollection<TKey> GetAllKeys();

    /// <summary>Number of records.</summary>
    int Count { get; }
}
