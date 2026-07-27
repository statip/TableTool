using System.Text.Json;
using TableTool.Cli.Model;

namespace TableTool.Cli.Export;

/// <summary>Exports parsed data to JSON files.</summary>
public sealed class JsonExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Export all tables in the model to JSON files.</summary>
    public ExportResult Export(DataModel model, string outputDirectory)
    {
        var dataDir = outputDirectory;
        Directory.CreateDirectory(dataDir);

        var errors = new List<string>();
        var filesWritten = new List<string>();

        foreach (var (tableName, table) in model.Tables)
        {
            try
            {
                var json = SerializeTable(table);
                var filePath = Path.Combine(dataDir, $"{tableName}.json");
                File.WriteAllText(filePath, json);
                filesWritten.Add(filePath);
            }
            catch (Exception ex)
            {
                errors.Add($"Error exporting table '{tableName}': {ex.Message}");
            }
        }

        return new ExportResult(filesWritten, errors);
    }

    private string SerializeTable(DataTable table)
    {
        var pkFields = table.Schema.GetPrimaryKeyFields();

        if (table.Schema.IsListMode)
        {
            // Array format for tables without PK
            var records = new List<Dictionary<string, object?>>();
            foreach (var row in table.Rows)
            {
                records.Add(SerializeRow(row, table.Schema));
            }
            return JsonSerializer.Serialize(new
            {
                isList = true,
                records
            }, JsonOptions);
        }

        // Record map format
        var recordMap = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var row in table.Rows)
        {
            var key = table.GetRowKey(table.Rows.IndexOf(row));
            recordMap[key] = SerializeRow(row, table.Schema);
        }

        if (pkFields.Count == 1)
        {
            return JsonSerializer.Serialize(new
            {
                keyColumn = pkFields[0],
                primaryKeyType = GetPkTypeString(table.Schema),
                records = recordMap
            }, JsonOptions);
        }
        else
        {
            return JsonSerializer.Serialize(new
            {
                keyColumn = pkFields,
                primaryKeyType = string.Join("|", pkFields.Select(f => GetFieldTypeString(table.Schema, f))),
                records = recordMap
            }, JsonOptions);
        }
    }

    private Dictionary<string, object?> SerializeRow(DataRow row, Schema.Models.TableDefinition schema)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var field in schema.Fields)
        {
            var cell = row.GetCell(field.Name);
            if (cell == null)
            {
                dict[field.Name] = null;
                continue;
            }

            dict[field.Name] = ConvertToJsonValue(cell.Value, field.ParsedType!);
        }
        return dict;
    }

    private static object? ConvertToJsonValue(object? value, Schema.Models.FieldType? fieldType)
    {
        if (value == null || fieldType == null) return null;

        return fieldType.Kind switch
        {
            Schema.Models.FieldTypeKind.Int => Convert.ToInt32(value),
            Schema.Models.FieldTypeKind.Long => Convert.ToInt64(value),
            Schema.Models.FieldTypeKind.Float => Convert.ToDouble(value),
            Schema.Models.FieldTypeKind.Bool => Convert.ToBoolean(value),
            Schema.Models.FieldTypeKind.String => value.ToString(),
            Schema.Models.FieldTypeKind.List => value is System.Collections.IList list
                ? list.Cast<object?>().Select(e => ConvertToJsonValue(e, fieldType.ElementType)).ToList()
                : value.ToString(),
            Schema.Models.FieldTypeKind.Map when value is System.Collections.IDictionary dict =>
                SerializeDictionary(dict, fieldType),
            Schema.Models.FieldTypeKind.Struct when value is System.Collections.IList structList =>
                structList.Cast<object?>().Select(e => ConvertStructToJson(e)).ToList(),
            _ => value.ToString()
        };
    }

    private static Dictionary<string, object?> SerializeDictionary(System.Collections.IDictionary dict, Schema.Models.FieldType fieldType)
    {
        var result = new Dictionary<string, object?>();
        foreach (var key in dict.Keys)
        {
            var strKey = key?.ToString() ?? "";
            if (key != null)
                result[strKey] = ConvertToJsonValue(dict[key], fieldType.ValueType);
        }
        return result;
    }

    private static Dictionary<string, object?>? ConvertStructToJson(object? structValue)
    {
        if (structValue is not Dictionary<string, object?> dict)
            return null;
        return dict.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static string GetPkTypeString(Schema.Models.TableDefinition table)
    {
        var pkFields = table.GetPrimaryKeyFields();
        var types = pkFields.Select(f =>
        {
            var field = table.Fields.Find(ff => ff.Name == f);
            return field?.ParsedType?.ToString() ?? "unknown";
        });
        return string.Join("|", types);
    }

    private static string GetFieldTypeString(Schema.Models.TableDefinition table, string fieldName)
    {
        var field = table.Fields.Find(f => f.Name == fieldName);
        return field?.ParsedType?.ToString() ?? "unknown";
    }
}

/// <summary>Result of JSON export.</summary>
public sealed class ExportResult
{
    public List<string> FilesWritten { get; }
    public List<string> Errors { get; }
    public bool Success => Errors.Count == 0;

    public ExportResult(List<string> filesWritten, List<string> errors)
    {
        FilesWritten = filesWritten;
        Errors = errors;
    }
}
