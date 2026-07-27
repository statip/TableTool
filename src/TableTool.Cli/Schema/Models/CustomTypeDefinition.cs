namespace TableTool.Cli.Schema.Models;

/// <summary>Definition of a user-defined custom type.</summary>
public sealed class CustomTypeDefinition
{
    /// <summary>Custom type name used in Excel/YAML (e.g. "DateTime", "Vector3").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Underlying storage type in Excel/JSON (e.g. "string", "int", "long").</summary>
    public string Storage { get; set; } = "string";

    /// <summary>C# type to use in generated code (e.g. "System.DateTime", "UnityEngine.Vector3").</summary>
    public string CSharp { get; set; } = string.Empty;

    /// <summary>Expression to parse storage value into custom type. {0} = raw string from JSON.</summary>
    public string? Parse { get; set; }

    /// <summary>Additional using/import statements needed (e.g. "System", "UnityEngine").</summary>
    public List<string>? Import { get; set; }
}
