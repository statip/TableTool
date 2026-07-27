namespace TableTool.Cli.Schema.Models;

/// <summary>Definition of a single field in a table schema.</summary>
public sealed class FieldDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string? Ref { get; set; }
    public List<FieldDefinition>? Struct { get; set; }
    public FieldType? ParsedType { get; set; }
    public FieldType? ParsedStructType { get; set; }

    public FieldDefinition Clone() => new()
    {
        Name = Name,
        Type = Type,
        Comment = Comment,
        Ref = Ref,
        Struct = Struct?.Select(s => s.Clone()).ToList(),
        ParsedType = ParsedType,
        ParsedStructType = ParsedStructType,
    };
}
