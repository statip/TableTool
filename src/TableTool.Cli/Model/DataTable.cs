using TableTool.Cli.Schema.Models;

namespace TableTool.Cli.Model;

/// <summary>Represents a parsed table with its data rows.</summary>
public sealed class DataTable
{
    public TableDefinition Schema { get; }
    public string Name => Schema.Name;
    public List<DataRow> Rows { get; } = new();

    public DataTable(TableDefinition schema)
    {
        Schema = schema;
    }

    /// <summary>Get a value from a specific row and field.</summary>
    public DataCell? GetCell(int rowIndex, string fieldName)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count) return null;
        return Rows[rowIndex].GetCell(fieldName);
    }

    /// <summary>Get all values for a specific field across all rows.</summary>
    public IEnumerable<DataCell?> GetFieldValues(string fieldName)
    {
        foreach (var row in Rows)
            yield return row.GetCell(fieldName);
    }

    /// <summary>Build a primary key string for a given row.</summary>
    public string GetRowKey(int rowIndex)
    {
        var pkFields = Schema.GetPrimaryKeyFields();
        if (pkFields.Count == 0) return string.Empty;
        if (pkFields.Count == 1)
        {
            var cell = GetCell(rowIndex, pkFields[0]);
            return cell?.Value?.ToString() ?? string.Empty;
        }

        var parts = pkFields.Select(f => GetCell(rowIndex, f)?.Value?.ToString() ?? "");
        return string.Join("|", parts);
    }
}

/// <summary>Represents a single data row as a dictionary of field name to cell.</summary>
public sealed class DataRow
{
    private readonly Dictionary<string, DataCell> _cells = new(StringComparer.OrdinalIgnoreCase);

    public DataCell? GetCell(string fieldName)
    {
        return _cells.TryGetValue(fieldName, out var cell) ? cell : null;
    }

    public void SetCell(string fieldName, DataCell cell)
    {
        _cells[fieldName] = cell;
    }

    public IEnumerable<KeyValuePair<string, DataCell>> AllCells => _cells;

    public bool HasField(string fieldName) => _cells.ContainsKey(fieldName);

    public int CellCount => _cells.Count;
}
