using YamlDotNet.Serialization;

namespace TableTool.Cli.Schema.Models;

/// <summary>Definition of a single table from the YAML schema.</summary>
public sealed class TableDefinition
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "file")]
    public string File { get; set; } = string.Empty;

    [YamlMember(Alias = "sheet")]
    public string? Sheet { get; set; }

    [YamlMember(Alias = "primary_key")]
    public object? PrimaryKey { get; set; }

    [YamlMember(Alias = "fields")]
    public List<FieldDefinition> Fields { get; set; } = new();

    /// <summary>Get PK field names. Returns list of field names (single or composite).</summary>
    public List<string> GetPrimaryKeyFields()
    {
        if (PrimaryKey == null) return new();
        if (PrimaryKey is string s) return new() { s };
        if (PrimaryKey is List<object> list)
            return list.Select(x => x.ToString()!).ToList();
        return new();
    }

    /// <summary>True if this table has a composite primary key.</summary>
    public bool IsCompositeKey => PrimaryKey is List<object>;

    /// <summary>True if no primary key defined (list mode).</summary>
    public bool IsListMode => PrimaryKey == null;
}
