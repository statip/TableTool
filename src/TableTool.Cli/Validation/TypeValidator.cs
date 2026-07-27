using TableTool.Cli.Model;
using TableTool.Cli.Schema.Models;

namespace TableTool.Cli.Validation;

/// <summary>Validates that cell values match their declared types.</summary>
public sealed class TypeValidator
{
    public List<ValidationError> Validate(DataModel model)
    {
        var errors = new List<ValidationError>();

        foreach (var (tableName, table) in model.Tables)
        {
            for (int rowIdx = 0; rowIdx < table.Rows.Count; rowIdx++)
            {
                var row = table.Rows[rowIdx];
                foreach (var field in table.Schema.Fields)
                {
                    var cell = row.GetCell(field.Name);
                    if (cell == null)
                    {
                        errors.Add(new ValidationError
                        {
                            TableName = tableName,
                            Row = rowIdx + 2,
                            Field = field.Name,
                            Message = $"Missing field '{field.Name}' in row {rowIdx + 2}.",
                            Severity = ErrorSeverity.Error,
                        });
                        continue;
                    }

                    // Check that the value is of the correct CLR type
                    if (cell.Value != null && !IsTypeMatch(cell.Value, field.ParsedType!))
                    {
                        errors.Add(new ValidationError
                        {
                            TableName = tableName,
                            Row = rowIdx + 2,
                            Field = field.Name,
                            Value = cell.Value?.ToString(),
                            ExpectedType = field.Type,
                            ActualType = cell.Value?.GetType().Name,
                            Message = $"Type mismatch: value '{cell.Value}' is not valid {field.Type}.",
                            Severity = ErrorSeverity.Error,
                        });
                    }
                }
            }
        }

        return errors;
    }

    private static bool IsTypeMatch(object value, FieldType fieldType)
    {
        return fieldType.Kind switch
        {
            FieldTypeKind.Bool => value is bool,
            FieldTypeKind.Int => value is int or long,
            FieldTypeKind.Long => value is long or int,
            FieldTypeKind.Float => value is float or double or int,
            FieldTypeKind.String => value is string,
            FieldTypeKind.List => value is System.Collections.IList,
            FieldTypeKind.Map => value is System.Collections.IDictionary,
            FieldTypeKind.Enum => value is string or int,
            FieldTypeKind.Struct => value is string or System.Collections.IList,
            _ => true
        };
    }
}
