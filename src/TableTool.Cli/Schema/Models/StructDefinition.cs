using YamlDotNet.Serialization;

namespace TableTool.Cli.Schema.Models;

/// <summary>Standalone struct/type definition, independent of any table.</summary>
public sealed class StructDefinition
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "generate_code")]
    public bool GenerateCode { get; set; } = true;

    [YamlMember(Alias = "fields")]
    public List<FieldDefinition> Fields { get; set; } = new();

    /// <summary>Parsed field types (set by SchemaLoader).</summary>
    [YamlIgnore]
    public List<FieldDefinition> ParsedFields { get; set; } = new();
}
