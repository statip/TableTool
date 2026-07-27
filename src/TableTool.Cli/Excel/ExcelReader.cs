using System.Text.RegularExpressions;
using ClosedXML.Excel;
using TableTool.Cli.Model;
using TableTool.Cli.Schema.Models;

namespace TableTool.Cli.Excel;

/// <summary>Reads Excel (.xlsx) files and produces DataTable objects.</summary>
public sealed class ExcelReader
{
    /// <summary>Read an Excel file for a given table definition.</summary>
    public ExcelReadResult ReadTable(TableDefinition tableDef, string excelDirectory, List<EnumDefinition> enums,
        List<CustomTypeDefinition>? customTypes = null)
    {
        var filePath = Path.Combine(excelDirectory, tableDef.File);
        if (!File.Exists(filePath))
            return ExcelReadResult.Fail($"Excel file not found: {filePath}");

        try
        {
            using var workbook = new XLWorkbook(filePath);
            IXLWorksheet? worksheet;

            if (!string.IsNullOrWhiteSpace(tableDef.Sheet))
            {
                if (!workbook.TryGetWorksheet(tableDef.Sheet, out worksheet))
                    return ExcelReadResult.Fail($"Sheet '{tableDef.Sheet}' not found in {filePath}.");
            }
            else
            {
                worksheet = workbook.Worksheet(1);
            }

            if (worksheet == null)
                return ExcelReadResult.Fail($"No worksheet found in {filePath}.");

            return ReadWorksheet(worksheet, tableDef, enums, customTypes);
        }
        catch (Exception ex)
        {
            return ExcelReadResult.Fail($"Error reading Excel file {filePath}: {ex.Message}");
        }
    }

    /// <summary>Parse header cell to extract field name and annotations.</summary>
    internal static HeaderInfo ParseHeader(string raw)
    {
        var result = new HeaderInfo();
        var trimmed = raw.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return result;

        // Check for ## comment columns - skip entirely
        if (trimmed.StartsWith("##"))
        {
            result.IsComment = true;
            return result;
        }

        var name = trimmed;

        // Extract #ref=TableName.FieldName suffix
        var refMatch = Regex.Match(name, @"#ref=(\w+)\.(\w+)$", RegexOptions.IgnoreCase);
        if (refMatch.Success)
        {
            result.RefTable = refMatch.Groups[1].Value;
            result.RefField = refMatch.Groups[2].Value;
            name = name.Substring(0, refMatch.Index);
        }

        // Check for # prefix = primary key marker
        if (name.StartsWith("#"))
        {
            result.IsPrimaryKey = true;
            name = name.Substring(1);
        }

        result.FieldName = name.Trim();
        result.HasValue = true;
        return result;
    }

    private ExcelReadResult ReadWorksheet(IXLWorksheet worksheet, TableDefinition tableDef, List<EnumDefinition> enums,
        List<CustomTypeDefinition>? customTypes = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var range = worksheet.RangeUsed();
        if (range == null)
            return ExcelReadResult.Fail($"Sheet '{worksheet.Name}' in {tableDef.File} is empty.");

        var firstRow = range.Row(1);
        var lastRow = range.LastRowUsed();
        var firstCol = range.Column(1);
        var lastCol = range.LastColumnUsed();

        if (firstRow == null || lastRow == null)
            return ExcelReadResult.Fail($"Unable to determine used range in '{worksheet.Name}'.");

        int firstColNum = firstCol?.ColumnNumber() ?? 1;
        int lastColNum = lastCol?.ColumnNumber() ?? firstColNum;

        // ── Row 1: Headers ──
        // Parse header annotations:  #Id  Name  Category#ref=ItemCategory.Id  ##note
        var columnInfo = new Dictionary<int, HeaderInfo>();
        var headerRow = firstRow;
        for (int col = firstColNum; col <= lastColNum; col++)
        {
            var raw = headerRow.Cell(col).GetString().Trim();
            var info = ParseHeader(raw);
            if (!info.HasValue || info.IsComment) continue;

            // Match against schema field (if schema provided)
            var schemaField = tableDef.Fields.Find(f =>
                string.Equals(f.Name, info.FieldName, StringComparison.OrdinalIgnoreCase));

            if (schemaField == null)
            {
                warnings.Add($"Column '{raw}' -> field '{info.FieldName}' not found in schema, skipping.");
                continue;
            }

            // Override PK flag from schema
            var pkFields = tableDef.GetPrimaryKeyFields();
            if (pkFields.Contains(info.FieldName))
                info.IsPrimaryKey = true;

            columnInfo[col] = info;
        }

        if (columnInfo.Count == 0)
            return ExcelReadResult.Fail($"No valid headers found in '{worksheet.Name}'.");

        // ── Row 2: Types ──
        // Optional type row — if present, validates/derives types from Excel
        var typeRow = worksheet.Row(firstRow.RowNumber() + 1);
        bool hasTypeRow = false;
        var typeOverrides = new Dictionary<string, FieldType>();

        // Check if Row 2 looks like a type row (not numeric data)
        int typeCheckCount = 0;
        int typeMatchCount = 0;
        foreach (var (col, info) in columnInfo)
        {
            var cellText = typeRow.Cell(col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(cellText)) continue;
            typeCheckCount++;
            // Heuristic: type row has words like int, string, float, bool, list<...>, map<...>
            if (Regex.IsMatch(cellText, @"^(int|long|float|double|string|bool|list|map|struct)", RegexOptions.IgnoreCase))
                typeMatchCount++;
        }

        // If most columns in Row 2 have type-like strings, treat it as a type row
        hasTypeRow = typeCheckCount > 0 && typeMatchCount >= typeCheckCount * 0.5;

        int dataStartRow;
        if (hasTypeRow)
        {
            dataStartRow = firstRow.RowNumber() + 2; // Row 3+

            foreach (var (col, info) in columnInfo)
            {
                var typeStr = typeRow.Cell(col).GetString().Trim();
                if (string.IsNullOrWhiteSpace(typeStr)) continue;

                try
                {
                    var parsedType = FieldType.Parse(typeStr, enums, customTypes);
                    typeOverrides[info.FieldName] = parsedType;

                    // Validate type against schema
                    var schemaField = tableDef.Fields.Find(f =>
                        string.Equals(f.Name, info.FieldName, StringComparison.OrdinalIgnoreCase));
                    if (schemaField?.ParsedType != null && schemaField.ParsedType.Kind != parsedType.Kind)
                    {
                        warnings.Add($"Column '{info.FieldName}': Excel type '{typeStr}' differs from schema type '{schemaField.Type}'. Using schema type.");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Column '{info.FieldName}': cannot parse Excel type '{typeStr}': {ex.Message}. Using schema type.");
                }
            }
        }
        else
        {
            dataStartRow = firstRow.RowNumber() + 1; // No type row, data starts Row 2
        }

        // ── Row 3 (or Row 2 if no type row): Comment row? ──
        // If next row starts with ##, skip it as a comment row
        int commentRowNum = hasTypeRow ? dataStartRow : firstRow.RowNumber() + 1;
        var possibleCommentRow = worksheet.Row(commentRowNum);
        bool hasCommentRow = false;
        foreach (var (col, _) in columnInfo)
        {
            var cellText = possibleCommentRow.Cell(col).GetString().Trim();
            if (cellText.StartsWith("##"))
            {
                hasCommentRow = true;
                break;
            }
        }
        if (hasCommentRow)
            dataStartRow = commentRowNum + 1;

        // ── Data rows ──
        var dataTable = new DataTable(tableDef);
        for (int rowIdx = dataStartRow; rowIdx <= lastRow.RowNumber(); rowIdx++)
        {
            var row = worksheet.Row(rowIdx);
            var dataRow = new DataRow();
            bool hasData = false;

            foreach (var (col, info) in columnInfo)
            {
                var cell = row.Cell(col);
                var schemaField = tableDef.Fields.Find(f =>
                    string.Equals(f.Name, info.FieldName, StringComparison.OrdinalIgnoreCase));
                if (schemaField == null) continue;

                // Use override type if Excel type row specified one, otherwise schema type
                var effectiveType = typeOverrides.GetValueOrDefault(info.FieldName, schemaField.ParsedType!);

                object? rawValue;
                if (cell.IsEmpty())
                {
                    rawValue = null;
                }
                else
                {
                    rawValue = cell.DataType switch
                    {
                        XLDataType.Number => cell.GetDouble(),
                        XLDataType.Boolean => cell.GetBoolean(),
                        _ => cell.GetString(),
                    };
                }

                var convertedValue = CellConverter.ConvertCellValue(rawValue, effectiveType, errors);
                dataRow.SetCell(info.FieldName, new DataCell(convertedValue, rawValue?.ToString()));

                if (rawValue != null && !string.IsNullOrWhiteSpace(rawValue.ToString()))
                    hasData = true;
            }

            if (hasData)
                dataTable.Rows.Add(dataRow);
        }

        return ExcelReadResult.CreateSuccess(dataTable, errors, warnings);
    }
}

/// <summary>Parsed header cell information.</summary>
public sealed class HeaderInfo
{
    public bool HasValue { get; set; }
    public bool IsComment { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public bool IsPrimaryKey { get; set; }
    public string? RefTable { get; set; }
    public string? RefField { get; set; }
    public bool HasForeignKey => RefTable != null && RefField != null;
}

/// <summary>Result of reading an Excel file.</summary>
public sealed class ExcelReadResult
{
    public bool Success { get; private set; }
    public DataTable? DataTable { get; private set; }
    public List<string> Errors { get; private set; } = new();
    public List<string> Warnings { get; private set; } = new();

    private ExcelReadResult() { }

    public static ExcelReadResult CreateSuccess(DataTable table, List<string> errors, List<string> warnings) => new()
    {
        Success = true,
        DataTable = table,
        Errors = errors,
        Warnings = warnings,
    };

    public static ExcelReadResult Fail(string error) => new()
    {
        Success = false,
        Errors = new() { error },
    };
}
