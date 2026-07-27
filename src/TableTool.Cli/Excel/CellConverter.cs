using System.Globalization;
using System.Text.Json;
using TableTool.Cli.Schema.Models;

namespace TableTool.Cli.Excel;

/// <summary>Converts raw cell values to typed values based on field type.</summary>
public sealed class CellConverter
{
    /// <summary>Convert a raw string value to the target type.</summary>
    public static object? Convert(string? rawValue, FieldType fieldType, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            // Return default for the type
            return fieldType.Kind switch
            {
                FieldTypeKind.Bool => false,
                FieldTypeKind.Int => 0,
                FieldTypeKind.Long => 0L,
                FieldTypeKind.Float => 0f,
                FieldTypeKind.String => string.Empty,
                FieldTypeKind.List => new List<object>(),
                FieldTypeKind.Map => new Dictionary<object, object>(),
                _ => null
            };
        }

        try
        {
            return fieldType.Kind switch
            {
                FieldTypeKind.Bool => ParseBool(rawValue),
                FieldTypeKind.Int => ParseInt(rawValue),
                FieldTypeKind.Long => ParseLong(rawValue),
                FieldTypeKind.Float => ParseFloat(rawValue),
                FieldTypeKind.String => rawValue,
                FieldTypeKind.List => ParseList(rawValue, fieldType.ElementType!, errors),
                FieldTypeKind.Map => ParseMap(rawValue, fieldType.KeyType!, fieldType.ValueType!, errors),
                FieldTypeKind.Enum => rawValue, // Enum values stored as strings
                FieldTypeKind.Custom => ConvertCustom(rawValue, fieldType, errors),
                FieldTypeKind.Struct => ParseStructList(rawValue, fieldType.StructFields, errors),
                _ => rawValue
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Conversion error: '{rawValue}' -> {fieldType}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Convert a raw (possibly numeric) cell value to the target type.</summary>
    public static object? ConvertCellValue(object? cellValue, FieldType fieldType, List<string> errors)
    {
        if (cellValue == null)
        {
            return fieldType.Kind switch
            {
                FieldTypeKind.Bool => false,
                FieldTypeKind.Int => 0,
                FieldTypeKind.Long => 0L,
                FieldTypeKind.Float => 0f,
                FieldTypeKind.String => string.Empty,
                FieldTypeKind.List => new List<object>(),
                FieldTypeKind.Map => new Dictionary<object, object>(),
                _ => null
            };
        }

        var rawStr = cellValue.ToString() ?? string.Empty;
        return Convert(rawStr, fieldType, errors);
    }

    private static bool ParseBool(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v == "true" || v == "1" || v == "yes") return true;
        if (v == "false" || v == "0" || v == "no") return false;
        throw new FormatException($"Cannot parse bool from '{value}'.");
    }

    private static int ParseInt(string value)
    {
        value = value.Trim().Replace(",", "");
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;
        throw new FormatException($"Cannot parse int from '{value}'.");
    }

    private static long ParseLong(string value)
    {
        value = value.Trim().Replace(",", "");
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            return l;
        throw new FormatException($"Cannot parse long from '{value}'.");
    }

    private static float ParseFloat(string value)
    {
        value = value.Trim().Replace(",", "");
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            return f;
        throw new FormatException($"Cannot parse float from '{value}'.");
    }

    private static List<object?> ParseList(string value, FieldType elementType, List<string> errors)
    {
        var trimmed = value.Trim();
        // Support JSON array format: [1,2,3] or ["a","b"]
        // Support bracket format: [武器, 新手] (Chinese brackets)
        // Support plain CSV: 1,2,3

        var items = new List<object?>();

        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            var inner = trimmed.Substring(1, trimmed.Length - 2);
            if (string.IsNullOrWhiteSpace(inner))
                return items;

            // Try JSON parse first
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    items.Add(ConvertJsonElement(elem, elementType, errors));
                }
                return items;
            }
            catch
            {
                // Not JSON - treat as comma-separated within brackets
                var parts = SplitByCommaOutsideQuotes(inner);
                foreach (var part in parts)
                {
                    var p = part.Trim().Trim('"').Trim('\'');
                    items.Add(Convert(p, elementType, errors));
                }
                return items;
            }
        }

        // Plain comma-separated
        var plainParts = SplitByCommaOutsideQuotes(trimmed);
        foreach (var part in plainParts)
        {
            var p = part.Trim().Trim('"').Trim('\'');
            items.Add(Convert(p, elementType, errors));
        }

        return items;
    }

    private static Dictionary<object, object?> ParseMap(string value, FieldType keyType, FieldType valueType, List<string> errors)
    {
        var result = new Dictionary<object, object?>();
        var trimmed = value.Trim();

        // Support JSON object format: {"atk":10, "def":5}
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("【") && trimmed.EndsWith("】")))
        {
            // Try JSON
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var k = Convert(prop.Name, keyType, errors);
                    var v = ConvertJsonElement(prop.Value, valueType, errors);
                    if (k != null)
                        result[k] = v;
                }
                return result;
            }
            catch
            {
                // Non-JSON dictionary in braces
                var inner = trimmed.Substring(1, trimmed.Length - 2);
                if (string.IsNullOrWhiteSpace(inner))
                    return result;
                ParseKeyValuePairs(inner, keyType, valueType, result, errors);
                return result;
            }
        }

        // key:value, key:value format
        ParseKeyValuePairs(trimmed, keyType, valueType, result, errors);
        return result;
    }

    private static void ParseKeyValuePairs(string text, FieldType keyType, FieldType valueType,
        Dictionary<object, object?> result, List<string> errors)
    {
        var pairs = SplitByCommaOutsideQuotes(text);
        foreach (var pair in pairs)
        {
            var colonIdx = pair.IndexOf(':');
            if (colonIdx < 0) continue;

            var keyStr = pair.Substring(0, colonIdx).Trim().Trim('"').Trim('\'');
            var valStr = pair.Substring(colonIdx + 1).Trim().Trim('"').Trim('\'');
            var k = Convert(keyStr, keyType, errors);
            var v = Convert(valStr, valueType, errors);
            if (k != null)
                result[k] = v;
        }
    }

    private static object? ConvertJsonElement(JsonElement elem, FieldType fieldType, List<string> errors)
    {
        return fieldType.Kind switch
        {
            FieldTypeKind.Bool => elem.GetBoolean(),
            FieldTypeKind.Int => elem.GetInt32(),
            FieldTypeKind.Long => elem.GetInt64(),
            FieldTypeKind.Float => elem.GetDouble(),
            FieldTypeKind.String => elem.GetString() ?? string.Empty,
            FieldTypeKind.List when fieldType.ElementType != null =>
                elem.EnumerateArray().Select(e => ConvertJsonElement(e, fieldType.ElementType, errors)).ToList(),
            FieldTypeKind.Map when fieldType.KeyType != null && fieldType.ValueType != null =>
                elem.EnumerateObject().ToDictionary(
                    p => Convert(p.Name, fieldType.KeyType, errors) ?? p.Name,
                    p => ConvertJsonElement(p.Value, fieldType.ValueType, errors)),
            _ => elem.GetString()
        };
    }

    private static List<Dictionary<string, object?>> ParseStructList(string value, List<Schema.Models.FieldDefinition>? structFields, List<string> errors)
    {
        var result = new List<Dictionary<string, object?>>();
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || structFields == null)
            return result;

        // Try parsing as JSON array
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var sf in structFields)
                    {
                        if (elem.TryGetProperty(sf.Name, out var propVal))
                        {
                            dict[sf.Name] = ConvertJsonElement(propVal, sf.ParsedType!, errors);
                        }
                    }
                    result.Add(dict);
                }
                return result;
            }
            catch
            {
                errors.Add($"Failed to parse struct list JSON: {trimmed}");
                return result;
            }
        }

        // Single object (not an array)
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var dict = new Dictionary<string, object?>();
            foreach (var sf in structFields)
            {
                if (doc.RootElement.TryGetProperty(sf.Name, out var propVal))
                {
                    dict[sf.Name] = ConvertJsonElement(propVal, sf.ParsedType!, errors);
                }
            }
            result.Add(dict);
        }
        catch
        {
            errors.Add($"Failed to parse struct JSON: {trimmed}");
        }

        return result;
    }

    private static object? ConvertCustom(string rawValue, FieldType fieldType, List<string> errors)
    {
        var storageType = fieldType.StorageType;
        if (storageType == null) return rawValue;
        // Delegate to storage type conversion - the custom type conversion happens at C# deserialization time
        return Convert(rawValue, storageType, errors);
    }

    private static List<string> SplitByCommaOutsideQuotes(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var inQuote = false;
        var start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' || c == '\'')
                inQuote = !inQuote;
            else if (!inQuote && (c == '[' || c == '{' || c == '('))
                depth++;
            else if (!inQuote && (c == ']' || c == '}' || c == ')'))
                depth--;
            else if (!inQuote && depth == 0 && c == ',')
            {
                parts.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }

        parts.Add(text.Substring(start));
        return parts;
    }
}
