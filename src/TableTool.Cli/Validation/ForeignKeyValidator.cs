using TableTool.Cli.Model;
using TableTool.Cli.Schema.Models;

namespace TableTool.Cli.Validation;

/// <summary>Validates that foreign key references exist in referenced tables.</summary>
public sealed class ForeignKeyValidator
{
    /// <summary>Validate FK refs in table fields.</summary>
    public List<ValidationError> Validate(DataModel model)
    {
        var errors = new List<ValidationError>();

        foreach (var (tableName, table) in model.Tables)
        {
            foreach (var field in table.Schema.Fields)
            {
                var refs = CollectRefs(field);
                foreach (var (refFieldName, refStr, refTableName) in refs)
                {
                    errors.AddRange(CheckRef(tableName, refFieldName, refStr, refTableName, model, table));
                }
            }
        }

        return errors;
    }

    /// <summary>Validate FK refs in table fields + standalone structs.</summary>
    public List<ValidationError> Validate(DataModel model, List<StructDefinition>? structs)
    {
        var errors = Validate(model);

        if (structs == null) return errors;

        // Also check FK refs inside struct definitions (against table data)
        foreach (var st in structs)
        {
            foreach (var field in st.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Ref)) continue;

                var refParts = field.Ref.Split('.');
                if (refParts.Length != 2) continue;

                var refTableName = refParts[0];
                var refFieldName = refParts[1];

                var refTable = model.GetTable(refTableName);
                if (refTable == null)
                {
                    var where = st.GenerateCode ? "struct" : "extern_type";
                    errors.Add(new ValidationError
                    {
                        TableName = $"{where} {st.Name}",
                        Field = field.Name,
                        Value = field.Ref,
                        Message = $"FK 引用的表 '{refTableName}' 不存在！\n" +
                                   $"  → 去 {where} '{st.Name}' 的 fields 里删掉 {field.Name} 这一行，或者删掉 ref: {field.Ref}",
                        Severity = ErrorSeverity.Error,
                    });
                    continue;
                }

                var validRefValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < refTable.Rows.Count; i++)
                {
                    var cell = refTable.Rows[i].GetCell(refFieldName);
                    if (cell?.Value != null)
                        validRefValues.Add(cell.Value.ToString()!);
                }

                // Check values in each table that uses this struct
                foreach (var (tableName, table) in model.Tables)
                {
                    foreach (var tf in table.Schema.Fields)
                    {
                        if (!MatchesStructRef(tf, st.Name)) continue;

                        // Struct values serialized as JSON in a single cell
                        for (int rowIdx = 0; rowIdx < table.Rows.Count; rowIdx++)
                        {
                            var cell = table.Rows[rowIdx].GetCell(tf.Name);
                            if (cell?.Value == null) continue;

                            // Try to extract struct field value from JSON data
                            var cellStr = cell.Value.ToString();
                            if (cellStr == null) continue;

                            // For struct values stored as list of dicts
                            var foundValues = ExtractStructFieldValues(cellStr, field.Name);
                            foreach (var val in foundValues)
                            {
                                if (!string.IsNullOrWhiteSpace(val) && !validRefValues.Contains(val))
                                {
                                    errors.Add(new ValidationError
                                    {
                                        TableName = tableName,
                                        Row = rowIdx + 2,
                                        Field = $"{tf.Name}.{field.Name}",
                                        Value = val,
                                        ExpectedType = $"Exists in {field.Ref}",
                                        Message = $"FK violation: '{val}' not found in {field.Ref} (struct {st.Name})",
                                        Severity = ErrorSeverity.Error,
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        return errors;
    }

    /// <summary>Collect FK refs from a field, including refs inside inline struct.</summary>
    private static List<(string fieldName, string refStr, string refTableName)> CollectRefs(
        FieldDefinition field)
    {
        var list = new List<(string, string, string)>();

        if (!string.IsNullOrWhiteSpace(field.Ref))
        {
            var parts = field.Ref.Split('.');
            if (parts.Length == 2)
                list.Add((field.Name, field.Ref, parts[0]));
        }

        // Inline struct refs
        if (field.ParsedStructType?.StructFields != null)
        {
            foreach (var sf in field.ParsedStructType.StructFields)
            {
                if (!string.IsNullOrWhiteSpace(sf.Ref))
                {
                    var parts = sf.Ref.Split('.');
                    if (parts.Length == 2)
                        list.Add((sf.Name, sf.Ref, parts[0]));
                }
            }
        }

        return list;
    }

    private static List<ValidationError> CheckRef(string tableName, string fieldName,
        string refStr, string refTableName, DataModel model, DataTable table)
    {
        var errors = new List<ValidationError>();

        var refTable = model.GetTable(refTableName);
        if (refTable == null)
        {
            errors.Add(new ValidationError
            {
                TableName = tableName,
                Field = fieldName,
                Value = refStr,
                Message = $"引用的表 '{refTableName}' 不存在！\n" +
                           $"  → 表 '{tableName}' 的字段 '{fieldName}' 有 ref: {refStr}，删掉这行 ref 或恢复此表",
                Severity = ErrorSeverity.Error,
            });
            return errors;
        }

        var refFieldName = refStr.Split('.')[1];
        var validRefValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < refTable.Rows.Count; i++)
        {
            var cell = refTable.Rows[i].GetCell(refFieldName);
            if (cell?.Value != null)
                validRefValues.Add(cell.Value.ToString()!);
        }

        for (int rowIdx = 0; rowIdx < table.Rows.Count; rowIdx++)
        {
            var row = table.Rows[rowIdx];
            var cell = row.GetCell(fieldName);
            if (cell?.Value == null) continue;

            var fkValue = cell.Value.ToString()!;
            if (!string.IsNullOrWhiteSpace(fkValue) && !validRefValues.Contains(fkValue))
            {
                errors.Add(new ValidationError
                {
                    TableName = tableName,
                    Row = rowIdx + 2,
                    Field = fieldName,
                    Value = fkValue,
                    ExpectedType = $"Exists in {refStr}",
                    Message = $"Foreign key violation: '{fkValue}' not found in {refStr}",
                    Severity = ErrorSeverity.Error,
                });
            }
        }

        return errors;
    }

    private static bool MatchesStructRef(FieldDefinition field, string structName)
    {
        if (field.ParsedType == null) return false;
        if (field.ParsedType.Kind == FieldTypeKind.Struct &&
            string.Equals(field.ParsedType.StructName, structName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (field.ParsedType.Kind == FieldTypeKind.List &&
            field.ParsedType.ElementType?.Kind == FieldTypeKind.Struct &&
            string.Equals(field.ParsedType.ElementType.StructName, structName, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static List<string> ExtractStructFieldValues(string json, string structFieldName)
    {
        var results = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.TryGetProperty(structFieldName, out var prop))
                        results.Add(prop.ToString());
                }
            }
            else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty(structFieldName, out var prop))
                    results.Add(prop.ToString());
            }
        }
        catch { /* not JSON, skip */ }
        return results;
    }
}
