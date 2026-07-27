using TableTool.Cli.Schema.Models;

namespace TableTool.Cli.Model;

/// <summary>In-memory representation of all parsed tables.</summary>
public sealed class DataModel
{
    private readonly Dictionary<string, DataTable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EnumDefinition> _enums = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, DataTable> Tables => _tables;
    public IReadOnlyDictionary<string, EnumDefinition> Enums => _enums;

    public DataModel() { }

    public DataModel(List<EnumDefinition> enums)
    {
        foreach (var e in enums)
            _enums[e.Name] = e;
    }

    public void AddTable(DataTable table)
    {
        _tables[table.Name] = table;
    }

    public DataTable? GetTable(string name)
    {
        return _tables.TryGetValue(name, out var t) ? t : null;
    }

    public bool HasTable(string name) => _tables.ContainsKey(name);

    public int TableCount => _tables.Count;

    public int TotalRows => _tables.Values.Sum(t => t.Rows.Count);
}
