using TableTool.Cli.Model;

namespace TableTool.Cli.Validation;

/// <summary>Orchestrates all validations and reports errors.</summary>
public sealed class SchemaValidator
{
    private readonly TypeValidator _typeValidator = new();
    private readonly PrimaryKeyValidator _pkValidator = new();
    private readonly ForeignKeyValidator _fkValidator = new();

    /// <summary>Run all validations on the data model.</summary>
    public ValidationResult Validate(DataModel model)
    {
        var allErrors = new List<ValidationError>();

        allErrors.AddRange(_typeValidator.Validate(model));
        allErrors.AddRange(_pkValidator.Validate(model));
        allErrors.AddRange(_fkValidator.Validate(model));

        return new ValidationResult(allErrors);
    }
}

/// <summary>Result of validation.</summary>
public sealed class ValidationResult
{
    public List<ValidationError> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    public ValidationResult(List<ValidationError> errors)
    {
        Errors = errors;
    }
}

/// <summary>A validation error or warning.</summary>
public sealed class ValidationError
{
    public string TableName { get; set; } = string.Empty;
    public int Row { get; set; }
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string? ExpectedType { get; set; }
    public string? ActualType { get; set; }
    public string Message { get; set; } = string.Empty;
    public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;

    public override string ToString()
    {
        var prefix = Severity == ErrorSeverity.Error ? "ERROR" : "WARN";
        var location = $"{TableName}";
        if (Row > 0) location += $":{Row}";
        if (!string.IsNullOrWhiteSpace(Field)) location += $".{Field}";
        if (!string.IsNullOrWhiteSpace(Value)) location += $" = {Value}";

        return $"[{prefix}] {location}\n  → {Message}";
    }

    public string ToShortString()
    {
        return $"[{Severity}] {TableName}: {Message}";
    }
}

public enum ErrorSeverity
{
    Warning,
    Error,
}
