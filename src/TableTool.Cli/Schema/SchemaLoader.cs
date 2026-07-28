using TableTool.Cli.Schema.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TableTool.Cli.Schema;

/// <summary>Loads and parses the YAML schema definition file.</summary>
public sealed class SchemaLoader
{
    /// <summary>Load a schema from a YAML file path.</summary>
    public SchemaLoadResult Load(string schemaPath)
    {
        if (!File.Exists(schemaPath))
        {
            return SchemaLoadResult.Fail($"Schema file not found: {schemaPath}");
        }

        try
        {
            var yaml = File.ReadAllText(schemaPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var doc = deserializer.Deserialize<SchemaDocument>(yaml);
            if (doc == null)
                return SchemaLoadResult.Fail("Failed to parse schema file: result was null.");

            if (doc.Tables == null || doc.Tables.Count == 0)
                return SchemaLoadResult.Fail("Schema file must define at least one table.");

            var errors = new List<string>();
            var enums = doc.Enums ?? new();
            var customTypes = doc.CustomTypes ?? new();
            var allStructs = doc.AllStructs;

            // Parse standalone struct definitions first
            foreach (var st in allStructs)
            {
                if (string.IsNullOrWhiteSpace(st.Name))
                {
                    errors.Add("Struct/extern_type missing 'name' field.");
                    continue;
                }
                foreach (var sf in st.Fields)
                {
                    if (string.IsNullOrWhiteSpace(sf.Name))
                    {
                        errors.Add($"Struct '{st.Name}' field missing 'name'.");
                        continue;
                    }
                    try
                    {
                        sf.ParsedType = FieldType.Parse(sf.Type, enums, customTypes, allStructs);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Struct '{st.Name}', field '{sf.Name}': {ex.Message}");
                    }
                }
            }

            // Parse field types for each table
            foreach (var table in doc.Tables)
            {
                if (string.IsNullOrWhiteSpace(table.Name))
                    errors.Add("Table missing 'name' field.");

                if (string.IsNullOrWhiteSpace(table.File))
                    errors.Add($"Table '{table.Name}' missing 'file' field.");

                if (table.Fields == null || table.Fields.Count == 0)
                {
                    errors.Add($"Table '{table.Name}' must have at least one field.");
                    continue;
                }

                foreach (var field in table.Fields)
                {
                    if (string.IsNullOrWhiteSpace(field.Name))
                    {
                        errors.Add($"Table '{table.Name}' has a field with missing 'name'.");
                        continue;
                    }

                    try
                    {
                        if (field.Struct is { Count: > 0 })
                        {
                            // Inline struct - parse struct sub-fields
                            var structFields = new List<FieldDefinition>();
                            foreach (var sf in field.Struct)
                            {
                                if (string.IsNullOrWhiteSpace(sf.Name))
                                {
                                    errors.Add($"Table '{table.Name}'.{field.Name} struct field missing 'name'.");
                                    continue;
                                }
                                sf.ParsedType = FieldType.Parse(sf.Type, enums, customTypes, allStructs);
                                structFields.Add(sf);
                            }
                            var structType = FieldType.Struct(structFields);
                            field.ParsedStructType = structType;

                            // If type is list<struct> (e.g. Excel type row says so), wrap in List
                            if (field.Type.Trim().StartsWith("list<", StringComparison.OrdinalIgnoreCase))
                                field.ParsedType = FieldType.List(structType);
                            else
                                field.ParsedType = structType;
                        }
                        else
                        {
                            field.ParsedType = FieldType.Parse(field.Type, enums, customTypes, allStructs);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Table '{table.Name}', field '{field.Name}': {ex.Message}");
                    }
                }
            }

            // Validate PK fields exist
            foreach (var table in doc.Tables)
            {
                var pkFields = table.GetPrimaryKeyFields();
                var fieldNames = new HashSet<string>(table.Fields.Select(f => f.Name));
                foreach (var pk in pkFields)
                {
                    if (!fieldNames.Contains(pk))
                        errors.Add($"Table '{table.Name}': primary key field '{pk}' not found in fields list.");
                }
            }

            if (errors.Count > 0)
                return SchemaLoadResult.Fail(errors);

            return SchemaLoadResult.CreateSuccess(doc, enums);
        }
        catch (Exception ex)
        {
            return SchemaLoadResult.Fail($"Error parsing schema file: {ex.Message}");
        }
    }

    /// <summary>Load only types (enums, custom_types, structs, extern_types) — no tables required.
    /// Used when schema is self-described by Excel headers.</summary>
    public SchemaLoadResult LoadTypes(string schemaPath)
    {
        if (!File.Exists(schemaPath))
            return SchemaLoadResult.Fail($"Types file not found: {schemaPath}");

        try
        {
            var yaml = File.ReadAllText(schemaPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var doc = deserializer.Deserialize<SchemaDocument>(yaml);
            if (doc == null)
                return SchemaLoadResult.Fail("Failed to parse types file.");

            var errors = new List<string>();
            var enums = doc.Enums ?? new();
            var customTypes = doc.CustomTypes ?? new();
            var allStructs = doc.AllStructs;

            // Parse struct field types
            foreach (var st in allStructs)
            {
                if (string.IsNullOrWhiteSpace(st.Name))
                {
                    errors.Add("Struct/extern_type missing 'name'.");
                    continue;
                }
                foreach (var sf in st.Fields)
                {
                    try
                    {
                        sf.ParsedType = FieldType.Parse(sf.Type, enums, customTypes, allStructs);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Struct '{st.Name}', field '{sf.Name}': {ex.Message}");
                    }
                }
            }

            if (errors.Count > 0)
                return SchemaLoadResult.Fail(errors);

            return SchemaLoadResult.CreateTypesSuccess(enums, customTypes, doc.Structs ?? new(), doc.ExternTypes ?? new());
        }
        catch (Exception ex)
        {
            return SchemaLoadResult.Fail($"Error parsing types file: {ex.Message}");
        }
    }
}

/// <summary>Result of loading a schema file.</summary>
public sealed class SchemaLoadResult
{
    public bool Success { get; private set; }
    public SchemaDocument? Document { get; private set; }
    public List<EnumDefinition> Enums { get; private set; } = new();
    public List<CustomTypeDefinition> CustomTypes { get; private set; } = new();
    public List<StructDefinition> Structs { get; private set; } = new();
    public List<StructDefinition> ExternTypes { get; private set; } = new();
    public List<string> Errors { get; private set; } = new();

    private SchemaLoadResult() { }

    public static SchemaLoadResult CreateSuccess(SchemaDocument doc, List<EnumDefinition> enums) => new()
    {
        Success = true,
        Document = doc,
        Enums = enums,
    };

    public static SchemaLoadResult CreateTypesSuccess(
        List<EnumDefinition> enums,
        List<CustomTypeDefinition> customTypes,
        List<StructDefinition> structs,
        List<StructDefinition> externTypes) => new()
    {
        Success = true,
        Enums = enums,
        CustomTypes = customTypes,
        Structs = structs,
        ExternTypes = externTypes,
    };

    public static SchemaLoadResult Fail(string error) => new()
    {
        Success = false,
        Errors = new() { error },
    };

    public static SchemaLoadResult Fail(List<string> errors) => new()
    {
        Success = false,
        Errors = errors,
    };
}
