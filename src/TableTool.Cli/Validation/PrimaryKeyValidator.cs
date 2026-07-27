using TableTool.Cli.Model;

namespace TableTool.Cli.Validation;

/// <summary>Validates primary key uniqueness and non-null constraints.</summary>
public sealed class PrimaryKeyValidator
{
    public List<ValidationError> Validate(DataModel model)
    {
        var errors = new List<ValidationError>();

        foreach (var (tableName, table) in model.Tables)
        {
            var pkFields = table.Schema.GetPrimaryKeyFields();
            if (pkFields.Count == 0) continue; // List mode, no PK validation

            var seenKeys = new HashSet<string>();

            // Validate each PK field exists
            foreach (var pkField in pkFields)
            {
                if (!table.Schema.Fields.Any(f => string.Equals(f.Name, pkField, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add(new ValidationError
                    {
                        TableName = tableName,
                        Field = pkField,
                        Message = $"Primary key field '{pkField}' not found in schema fields.",
                        Severity = ErrorSeverity.Error,
                    });
                }
            }

            for (int rowIdx = 0; rowIdx < table.Rows.Count; rowIdx++)
            {
                var row = table.Rows[rowIdx];
                var rowKey = table.GetRowKey(rowIdx);

                // Check null/empty PK
                if (string.IsNullOrWhiteSpace(rowKey))
                {
                    errors.Add(new ValidationError
                    {
                        TableName = tableName,
                        Row = rowIdx + 2,
                        Message = $"Primary key is null or empty in row {rowIdx + 2}.",
                        Severity = ErrorSeverity.Error,
                    });
                    continue;
                }

                // Check duplicate PK
                if (!seenKeys.Add(rowKey))
                {
                    errors.Add(new ValidationError
                    {
                        TableName = tableName,
                        Row = rowIdx + 2,
                        Value = rowKey,
                        Message = $"Duplicate primary key '{rowKey}' in row {rowIdx + 2}.",
                        Severity = ErrorSeverity.Error,
                    });
                }

                // For composite keys, check separator doesn't collide
                if (pkFields.Count > 1 && rowKey.Contains("|"))
                {
                    // Check if any PK field value contains '|'
                    foreach (var pkField in pkFields)
                    {
                        var cell = row.GetCell(pkField);
                        if (cell?.Value?.ToString()?.Contains("|") == true)
                        {
                            errors.Add(new ValidationError
                            {
                                TableName = tableName,
                                Row = rowIdx + 2,
                                Field = pkField,
                                Value = cell.Value?.ToString(),
                                Message = $"Composite PK field '{pkField}' contains separator '|' in value '{cell.Value}'.",
                                Severity = ErrorSeverity.Error,
                            });
                        }
                    }
                }
            }
        }

        return errors;
    }
}
