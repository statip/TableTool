using TableTool.Cli.Model;

namespace TableTool.Cli.Validation;

/// <summary>Validates that foreign key references exist in referenced tables.</summary>
public sealed class ForeignKeyValidator
{
    public List<ValidationError> Validate(DataModel model)
    {
        var errors = new List<ValidationError>();

        foreach (var (tableName, table) in model.Tables)
        {
            foreach (var field in table.Schema.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Ref)) continue;

                var refParts = field.Ref.Split('.');
                if (refParts.Length != 2)
                {
                    errors.Add(new ValidationError
                    {
                        TableName = tableName,
                        Field = field.Name,
                        Message = $"Invalid ref format '{field.Ref}'. Expected 'TableName.FieldName'.",
                        Severity = ErrorSeverity.Error,
                    });
                    continue;
                }

                var refTableName = refParts[0];
                var refFieldName = refParts[1];

                // Check referenced table exists
                var refTable = model.GetTable(refTableName);
                if (refTable == null)
                {
                    errors.Add(new ValidationError
                    {
                        TableName = tableName,
                        Field = field.Name,
                        Value = field.Ref,
                        Message = $"Referenced table '{refTableName}' not found. Ref: {field.Ref}",
                        Severity = ErrorSeverity.Error,
                    });
                    continue;
                }

                // Build a set of valid reference values
                var validRefValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < refTable.Rows.Count; i++)
                {
                    var cell = refTable.Rows[i].GetCell(refFieldName);
                    if (cell?.Value != null)
                        validRefValues.Add(cell.Value.ToString()!);
                }

                // Check each row's FK value
                for (int rowIdx = 0; rowIdx < table.Rows.Count; rowIdx++)
                {
                    var row = table.Rows[rowIdx];
                    var cell = row.GetCell(field.Name);
                    if (cell?.Value == null) continue;

                    var fkValue = cell.Value.ToString()!;
                    if (!string.IsNullOrWhiteSpace(fkValue) && !validRefValues.Contains(fkValue))
                    {
                        errors.Add(new ValidationError
                        {
                            TableName = tableName,
                            Row = rowIdx + 2,
                            Field = field.Name,
                            Value = fkValue,
                            ExpectedType = $"Exists in {field.Ref}",
                            Message = $"Foreign key violation: '{fkValue}' not found in {field.Ref}",
                            Severity = ErrorSeverity.Error,
                        });
                    }
                }
            }
        }

        return errors;
    }
}
