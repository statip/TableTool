using TableTool.Cli.CodeGen;
using TableTool.Cli.Excel;
using TableTool.Cli.Export;
using TableTool.Cli.Model;
using TableTool.Cli.Schema;
using TableTool.Cli.Schema.Models;
using TableTool.Cli.Validation;

namespace TableTool.Cli.Commands;

/// <summary>The main build command that orchestrates the pipeline.</summary>
public sealed class BuildCommand
{
    private readonly string _schemaPath;
    private readonly string _excelDir;
    private readonly string _outputDir;
    private readonly string _dataDir;
    private readonly string _genDir;
    private readonly string _namespace;

    public BuildCommand(
        string schemaPath,
        string excelDir,
        string outputDir,
        string dataDir,
        string genDir,
        string ns)
    {
        _schemaPath = schemaPath;
        _excelDir = excelDir;
        _outputDir = outputDir;
        _dataDir = dataDir;
        _genDir = genDir;
        _namespace = ns;
    }

    public int Execute()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"TableTool Build - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine(new string('-', 60));

        // Step 1: Load types (optional schema/types.yaml)
        var enums = new List<EnumDefinition>();
        var customTypes = new List<CustomTypeDefinition>();
        var structDefs = new List<StructDefinition>();
        var externDefs = new List<StructDefinition>();

        if (File.Exists(_schemaPath))
        {
            var fi = new FileInfo(_schemaPath);
            if (fi.Length > 0)
            {
                var schemaLoader = new SchemaLoader();
                var schemaResult = schemaLoader.LoadTypes(_schemaPath);
                if (schemaResult.Success)
                {
                    enums = schemaResult.Enums;
                    customTypes = schemaResult.CustomTypes;
                    structDefs = schemaResult.Structs;
                    externDefs = schemaResult.ExternTypes;
                    Console.WriteLine($"Types: {enums.Count} enums, {customTypes.Count} custom, {structDefs.Count} structs, {externDefs.Count} extern");
                }
            }
        }
        else if (!File.Exists(_schemaPath))
        {
            Console.WriteLine("No types.yaml");
        }

        var allStructs = new List<StructDefinition>();
        allStructs.AddRange(structDefs);
        allStructs.AddRange(externDefs);

        // Step 2: Discover tables from Excel files
        if (!Directory.Exists(_excelDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Excel directory not found: {_excelDir}");
            Console.ResetColor();
            return 1;
        }

        var xlsxFiles = Directory.GetFiles(_excelDir, "*.xlsx").OrderBy(f => f).ToList();
        if (xlsxFiles.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"No .xlsx files found in '{_excelDir}'");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"Found {xlsxFiles.Count} Excel files");

        // Step 3: Parse Excel files into DataModel
        Console.WriteLine("\nParsing Excel files...");
        var dataModel = new DataModel(enums);
        var reader = new ExcelReader();
        var tableDefs = new List<TableDefinition>();
        bool hasParseErrors = false;

        foreach (var xlsxPath in xlsxFiles)
        {
            var fileName = Path.GetFileName(xlsxPath);
            var tableName = Path.GetFileNameWithoutExtension(xlsxPath);

            Console.Write($"  {tableName} ({fileName})... ");

            // Build TableDefinition from Excel headers
            var tableDef = reader.BuildTableDefinition(xlsxPath, enums, customTypes, allStructs);
            if (tableDef == null)
            {
                Console.WriteLine("FAILED (cannot parse headers)");
                hasParseErrors = true;
                continue;
            }
            tableDef.File = fileName;

            var result = reader.ReadTable(tableDef, _excelDir, enums, customTypes, allStructs);
            if (!result.Success)
            {
                Console.WriteLine("FAILED");
                foreach (var err in result.Errors)
                    Console.Error.WriteLine($"    [ERROR] {err}");
                hasParseErrors = true;
                continue;
            }

            Console.WriteLine(result.Errors.Count > 0
                ? $"OK ({result.DataTable!.Rows.Count} rows, {result.Errors.Count} warnings)"
                : $"OK ({result.DataTable!.Rows.Count} rows)");

            foreach (var warn in result.Warnings)
                Console.WriteLine($"    [WARN] {warn}");

            dataModel.AddTable(result.DataTable!);
            tableDefs.Add(tableDef);
        }

        if (hasParseErrors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nBuild FAILED due to parse errors.");
            Console.ResetColor();
            return 1;
        }

        // Step 4: Validate
        Console.WriteLine("\nValidating data...");
        var validator = new SchemaValidator();
        var validationResult = validator.Validate(dataModel, allStructs);

        if (!validationResult.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAILED ({validationResult.Errors.Count} errors)");
            foreach (var error in validationResult.Errors)
            {
                var prefix = error.Severity == ErrorSeverity.Error ? "ERROR" : "WARN";
                Console.Error.WriteLine($"  [{prefix}] {error}");
            }
            Console.ResetColor();
            return 1;
        }
        Console.WriteLine("  All validations passed.");

        // Clean output dirs (but preserve TableSettings.cs)
        var outputDataPath = Path.Combine(_outputDir, _dataDir);
        var outputGenPath = Path.Combine(_outputDir, _genDir);
        var oldSettingsPath = Path.Combine(outputGenPath, "TableSettings.cs");
        string? oldSettings = File.Exists(oldSettingsPath) ? File.ReadAllText(oldSettingsPath) : null;
        if (Directory.Exists(outputDataPath)) Directory.Delete(outputDataPath, true);
        if (Directory.Exists(outputGenPath)) Directory.Delete(outputGenPath, true);
        Directory.CreateDirectory(outputDataPath);
        Directory.CreateDirectory(outputGenPath);
        if (oldSettings != null) File.WriteAllText(oldSettingsPath, oldSettings);

        // Step 5: Export JSON
        Console.WriteLine("\nExporting JSON...");
        var exporter = new JsonExporter();
        var exportResult = exporter.Export(dataModel, outputDataPath);

        if (!exportResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAILED");
            foreach (var err in exportResult.Errors)
                Console.Error.WriteLine($"  [ERROR] {err}");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"  {exportResult.FilesWritten.Count} JSON files");
        foreach (var file in exportResult.FilesWritten)
        {
            var fi = new FileInfo(file);
            Console.WriteLine($"    {fi.Name} ({fi.Length} bytes)");
        }

        // Step 6: Generate C# code
        Console.WriteLine("\nGenerating C# code...");
        var classGenerator = new CSharpClassGenerator(_namespace);
        var tablesGenerator = new TablesGenerator(_namespace);
        int genFileCount = 0;

        foreach (var tableDef in tableDefs)
        {
            var code = classGenerator.Generate(tableDef, enums);
            var fileName = $"{CSharpClassGenerator.GetTableClassName(tableDef.Name)}.cs";
            File.WriteAllText(Path.Combine(outputGenPath, fileName), code);
            genFileCount++;
            Console.WriteLine($"  {fileName}");
        }

        // Tables.cs
        var tablesCode = tablesGenerator.Generate(tableDefs, enums, customTypes);
        File.WriteAllText(Path.Combine(outputGenPath, "Tables.cs"), tablesCode);
        genFileCount++;
        Console.WriteLine("  Tables.cs");

        // Standalone structs (generate_code: true)
        var structsToGen = structDefs.Where(s => s.GenerateCode).ToList();
        foreach (var st in structsToGen)
        {
            var code = classGenerator.GenerateStruct(st, _namespace);
            var fileName = $"{st.Name}.cs";
            File.WriteAllText(Path.Combine(outputGenPath, fileName), code);
            genFileCount++;
            Console.WriteLine($"  {fileName}");
        }
        if (structsToGen.Count > 0)
            Console.WriteLine($"  ({structsToGen.Count} standalone structs)");

        // Generate TableSettings.cs (only once, not overwritten)
        var settingsPath = Path.Combine(outputGenPath, "TableSettings.cs");
        if (!File.Exists(settingsPath))
        {
            var settingsCode = tablesGenerator.GenerateSettings(_namespace);
            File.WriteAllText(settingsPath, settingsCode);
            genFileCount++;
            Console.WriteLine("  TableSettings.cs (first time only)");
        }

        sw.Stop();
        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"Build completed successfully in {sw.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"  Tables: {dataModel.TableCount}");
        Console.WriteLine($"  Records: {dataModel.TotalRows}");
        Console.WriteLine($"  JSON files: {exportResult.FilesWritten.Count}");
        Console.WriteLine($"  C# files: {genFileCount}");

        return 0;
    }
}
