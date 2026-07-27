namespace TableTool.Cli.Model;

/// <summary>Represents a single parsed cell value.</summary>
public sealed class DataCell
{
    public object? Value { get; set; }
    public string? RawValue { get; set; }

    public DataCell() { }

    public DataCell(object? value, string? rawValue = null)
    {
        Value = value;
        RawValue = rawValue ?? value?.ToString();
    }

    public T? GetValue<T>() => Value is T t ? t : default;

    public override string ToString() => Value?.ToString() ?? "(null)";
}
