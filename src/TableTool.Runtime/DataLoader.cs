using System.Reflection;
using System.Text.Json;

namespace TableTool.Runtime;

/// <summary>Loads JSON data files into typed table classes.</summary>
public sealed class DataLoader
{
    /// <summary>Directory containing JSON data files.</summary>
    public string DataPath { get; set; } = "./data";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Load a single table from its JSON file.</summary>
    public TTable Load<TTable, TKey, TRecord>(string name)
        where TTable : class, new()
        where TKey : notnull
        where TRecord : class
    {
        var jsonPath = Path.Combine(DataPath, $"{name}.json");
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"Data file not found: {jsonPath}");

        var json = File.ReadAllText(jsonPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var table = new TTable();

        // Get the internal Load method
        var loadMethod = typeof(TTable).GetMethod("Load",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (loadMethod == null)
            throw new InvalidOperationException($"Table type {typeof(TTable).Name} does not have an internal Load method.");

        var recordsProp = root.GetProperty("records");

        // Check if list format
        if (root.TryGetProperty("isList", out var isList) && isList.GetBoolean())
        {
            // Array format: deserialize as list, convert to dict with string keys
            var records = JsonSerializer.Deserialize<List<TRecord>>(recordsProp.GetRawText(), JsonOptions)!;
            var dict = new Dictionary<string, TRecord>();
            for (int i = 0; i < records.Count; i++)
                dict[i.ToString()] = records[i];
            loadMethod.Invoke(table, new object[] { dict });
        }
        else
        {
            // Record map format
            var records = JsonSerializer.Deserialize<Dictionary<TKey, TRecord>>(recordsProp.GetRawText(), JsonOptions)!;
            loadMethod.Invoke(table, new object[] { records! });
        }

        return table;
    }
}
