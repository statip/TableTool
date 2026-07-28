using YamlDotNet.Serialization;

namespace TableTool.Cli.Schema.Models;

/// <summary>Top-level YAML schema document.</summary>
public sealed class SchemaDocument
{
    [YamlMember(Alias = "tables")]
    public List<TableDefinition> Tables { get; set; } = new();

    [YamlMember(Alias = "enums")]
    public List<EnumDefinition>? Enums { get; set; }

    [YamlMember(Alias = "custom_types")]
    public List<CustomTypeDefinition>? CustomTypes { get; set; }

    [YamlMember(Alias = "structs")]
    public List<StructDefinition>? Structs { get; set; }

    [YamlMember(Alias = "extern_types")]
    public List<StructDefinition>? ExternTypes { get; set; }

    /// <summary>Get all struct-like definitions (both structs and extern_types).</summary>
    [YamlIgnore]
    public List<StructDefinition> AllStructs
    {
        get
        {
            var list = new List<StructDefinition>();
            if (Structs != null) list.AddRange(Structs);
            if (ExternTypes != null) list.AddRange(ExternTypes);
            return list;
        }
    }
}
