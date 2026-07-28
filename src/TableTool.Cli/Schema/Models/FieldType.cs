namespace TableTool.Cli.Schema.Models;

/// <summary>Represents the parsed type of a field.</summary>
public enum FieldTypeKind
{
    Bool,
    Int,
    Long,
    Float,
    String,
    List,
    Map,
    Struct,
    Enum,
    Custom,  // User-defined custom type
}

/// <summary>Parsed field type information.</summary>
public sealed class FieldType
{
    public FieldTypeKind Kind { get; }
    public FieldType? ElementType { get; private set; }       // For List<T>
    public FieldType? KeyType { get; private set; }           // For Map<K,V>
    public FieldType? ValueType { get; private set; }         // For Map<K,V>
    public string? EnumName { get; private set; }             // For Enum type
    public List<FieldDefinition>? StructFields { get; private set; } // For inline struct
    public string? CustomTypeName { get; private set; }       // For Custom type (alias)
    public CustomTypeDefinition? CustomDefinition { get; set; } // Full custom type definition
    public string? StructName { get; private set; }           // For standalone struct type
    public StructDefinition? StructDefinition { get; set; }   // Struct definition reference

    private FieldType(FieldTypeKind kind)
    {
        Kind = kind;
    }

    public static FieldType Bool() => new(FieldTypeKind.Bool);
    public static FieldType Int() => new(FieldTypeKind.Int);
    public static FieldType Long() => new(FieldTypeKind.Long);
    public static FieldType Float() => new(FieldTypeKind.Float);
    public static FieldType String() => new(FieldTypeKind.String);
    public static FieldType Enum(string name) => new(FieldTypeKind.Enum) { EnumName = name };

    public static FieldType List(FieldType elementType) =>
        new(FieldTypeKind.List) { ElementType = elementType };

    public static FieldType Map(FieldType keyType, FieldType valueType) =>
        new(FieldTypeKind.Map) { KeyType = keyType, ValueType = valueType };

    public static FieldType Struct(List<FieldDefinition> fields) =>
        new(FieldTypeKind.Struct) { StructFields = fields };

    public static FieldType Custom(string name, CustomTypeDefinition def) =>
        new(FieldTypeKind.Custom) { CustomTypeName = name, CustomDefinition = def };

    public static FieldType StructRef(string name, StructDefinition def) =>
        new(FieldTypeKind.Struct) { StructName = name, StructDefinition = def, StructFields = def.Fields };

    /// <summary>Underlying storage FieldType (for Custom types).</summary>
    public FieldType? StorageType
    {
        get
        {
            if (Kind != FieldTypeKind.Custom || CustomDefinition == null) return null;
            return Parse(CustomDefinition.Storage, null);
        }
    }

    /// <summary>Parse a type string like "int", "list&lt;string&gt;", "map&lt;string,int&gt;", "ElementType" (enum), "DateTime" (custom).</summary>
    public static FieldType Parse(string typeStr, List<EnumDefinition>? enums = null,
        List<CustomTypeDefinition>? customTypes = null, List<StructDefinition>? structs = null)
    {
        var trimmed = typeStr.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("Type string cannot be empty.");

        // Check for list<T>
        if (trimmed.StartsWith("list<", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(">"))
        {
            var inner = trimmed.Substring(5, trimmed.Length - 6);
            return List(Parse(inner, enums, customTypes, structs));
        }

        // Check for map<K,V>
        if (trimmed.StartsWith("map<", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(">"))
        {
            var inner = trimmed.Substring(4, trimmed.Length - 5);
            var commaIdx = inner.IndexOf(',');
            if (commaIdx < 0)
                throw new ArgumentException($"Invalid map type: '{typeStr}'. Expected format: map<K,V>");
            var keyTypeStr = inner.Substring(0, commaIdx).Trim();
            var valTypeStr = inner.Substring(commaIdx + 1).Trim();
            return Map(Parse(keyTypeStr, enums, customTypes, structs), Parse(valTypeStr, enums, customTypes, structs));
        }

        // Check known primitives
        var lower = trimmed.ToLowerInvariant();
        if (lower is "bool" or "int" or "long" or "float" or "double" or "string" or "struct")
        {
            return lower switch
            {
                "bool" => Bool(),
                "int" => Int(),
                "long" => Long(),
                "float" or "double" => Float(),
                "string" => String(),
                "struct" => Struct(new List<FieldDefinition>()),
                _ => throw new ArgumentException($"Unknown type: '{typeStr}'.")
            };
        }

        // Check enums
        var enumResult = TryParseEnum(trimmed, enums);
        if (enumResult != null) return enumResult;

        // Check custom types
        var customResult = TryParseCustom(trimmed, customTypes);
        if (customResult != null) return customResult;

        // Check standalone structs
        var structResult = TryParseStruct(trimmed, structs);
        if (structResult != null) return structResult;

        throw new ArgumentException($"Unknown type: '{typeStr}'. " +
            "Available primitives: bool, int, long, float, double, string. " +
            "Support list<T>, map<K,V>.");
    }

    private static FieldType? TryParseEnum(string name, List<EnumDefinition>? enums)
    {
        if (enums == null) return null;
        foreach (var e in enums)
        {
            if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                return Enum(e.Name);
        }
        return null;
    }

    private static FieldType? TryParseStruct(string name, List<StructDefinition>? structs)
    {
        if (structs == null) return null;
        foreach (var s in structs)
        {
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                return StructRef(s.Name, s);
        }
        return null;
    }

    private static FieldType? TryParseCustom(string name, List<CustomTypeDefinition>? customTypes)
    {
        if (customTypes == null) return null;
        foreach (var c in customTypes)
        {
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                return Custom(c.Name, c);
        }
        return null;
    }

    public string ToCSharpType()
    {
        return Kind switch
        {
            FieldTypeKind.Bool => "bool",
            FieldTypeKind.Int => "int",
            FieldTypeKind.Long => "long",
            FieldTypeKind.Float => "float",
            FieldTypeKind.String => "string",
            FieldTypeKind.List => $"List<{ElementType!.ToCSharpType()}>",
            FieldTypeKind.Map => $"Dictionary<{KeyType!.ToCSharpType()},{ValueType!.ToCSharpType()}>",
            FieldTypeKind.Enum => EnumName!,
            FieldTypeKind.Struct when StructName != null => StructName,
            FieldTypeKind.Struct => "object",
            FieldTypeKind.Custom => CustomDefinition?.CSharp ?? CustomTypeName ?? "object",
            _ => "object"
        };
    }

    public string ToJsonType()
    {
        return Kind switch
        {
            FieldTypeKind.Bool => "bool",
            FieldTypeKind.Int => "int",
            FieldTypeKind.Long => "long",
            FieldTypeKind.Float => "float",
            FieldTypeKind.String => "string",
            FieldTypeKind.List => $"list<{ElementType!.ToJsonType()}>",
            FieldTypeKind.Map => $"map<{KeyType!.ToJsonType()},{ValueType!.ToJsonType()}>",
            FieldTypeKind.Enum => EnumName!,
            FieldTypeKind.Struct => "struct",
            FieldTypeKind.Custom => CustomDefinition?.Storage ?? "string",
            _ => "object"
        };
    }

    public override string ToString() => Kind switch
    {
        FieldTypeKind.List => $"list<{ElementType}>",
        FieldTypeKind.Map => $"map<{KeyType},{ValueType}>",
        FieldTypeKind.Enum => $"enum({EnumName})",
        FieldTypeKind.Custom => $"{CustomTypeName}({CustomDefinition?.Storage})",
        FieldTypeKind.Struct when StructName != null => StructName,
        _ => Kind.ToString().ToLowerInvariant()
    };

    /// <summary>Default CLR value for this type.</summary>
    public string GetDefaultValue()
    {
        if (Kind == FieldTypeKind.Custom && CustomDefinition?.CSharp != null)
        {
            var csType = CustomDefinition.CSharp;
            if (csType == "string") return "string.Empty";
            if (csType.Contains("DateTime")) return "default";
            if (csType.Contains("Vector")) return "default";
            return "default!";
        }
        return Kind switch
        {
            FieldTypeKind.Bool => "false",
            FieldTypeKind.Int => "0",
            FieldTypeKind.Long => "0L",
            FieldTypeKind.Float => "0f",
            FieldTypeKind.String => "string.Empty",
            FieldTypeKind.List => "new()",
            FieldTypeKind.Map => "new()",
            FieldTypeKind.Struct => "null!",
            FieldTypeKind.Enum => "default",
            _ => "default"
        };
    }
}
