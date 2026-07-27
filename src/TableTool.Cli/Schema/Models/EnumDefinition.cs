using YamlDotNet.Serialization;

namespace TableTool.Cli.Schema.Models;

/// <summary>Definition of an enum type from the YAML schema.</summary>
public sealed class EnumDefinition
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "values")]
    public Dictionary<string, int> Values { get; set; } = new();
}
