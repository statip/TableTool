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
}
