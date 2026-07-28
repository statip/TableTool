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

    /// <summary>Execute the build pipeline: schema → parse → validate → export → codegen.</summary>
    public int Execute()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"TableTool Build - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine(new string('-', 60));

        // Step 1: Load schema
        Console.Write("Loading schema... ");
        var schemaLoader = new SchemaLoader();
        var schemaResult = schemaLoader.Load(_schemaPath);
        if (!schemaResult.Success)
        {
            Console.WriteLine("FAILED");
            foreach (var err in schemaResult.Errors)
                Console.Error.WriteLine($"  [ERROR] {err}");
            return 1;
        }
        Console.WriteLine($"OK ({schemaResult.Document!.Tables.Count} tables, {schemaResult.Enums.Count} enums)");

        foreach (var t in schemaResult.Document.Tables)
        {
            Console.WriteLine($"  DEBUG: Table '{t.Name}' PK type={t.PrimaryKey?.GetType().Name ?? "null"}, value={t.PrimaryKey}, IsListMode={t.IsListMode}");
        }

        // Step 2: Parse Excel files
        Console.WriteLine("\nParsing Excel files...");
        var dataModel = new DataModel(schemaResult.Enums);
        var reader = new ExcelReader();
        bool hasParseErrors = false;

        foreach (var tableDef in schemaResult.Document.Tables)
        {
            Console.Write($"  {tableDef.Name} ({tableDef.File})... ");
            var result = reader.ReadTable(tableDef, _excelDir, schemaResult.Enums,
                schemaResult.Document.CustomTypes);

            if (!result.Success)
            {
                Console.WriteLine("FAILED");
                foreach (var err in result.Errors)
                    Console.Error.WriteLine($"    [ERROR] {err}");
                hasParseErrors = true;
                continue;
            }

            if (result.Errors.Count > 0)
            {
                Console.WriteLine($"OK ({result.DataTable!.Rows.Count} rows, {result.Errors.Count} warnings)");
                foreach (var err in result.Errors)
                    Console.WriteLine($"    [WARN] {err}");
            }
            else
            {
                Console.WriteLine($"OK ({result.DataTable!.Rows.Count} rows)");
            }

            dataModel.AddTable(result.DataTable);

            foreach (var warn in result.Warnings)
                Console.WriteLine($"    [WARN] {warn}");
        }

        if (hasParseErrors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nBuild FAILED due to parse errors.");
            Console.ResetColor();
            return 1;
        }

        // Step 3: Validate
        Console.WriteLine("\nValidating data...");
        var validator = new SchemaValidator();
        var validationResult = validator.Validate(dataModel);

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

        // Step 4: Export JSON
        Console.WriteLine("\nExporting JSON...");
        var exporter = new JsonExporter();
        var outputDataPath = Path.Combine(_outputDir, _dataDir);
        var exportResult = exporter.Export(dataModel, outputDataPath);

        if (!exportResult.Success)
        {
            Console.WriteLine("FAILED");
            foreach (var err in exportResult.Errors)
                Console.Error.WriteLine($"  [ERROR] {err}");
            return 1;
        }

        Console.WriteLine($"  {exportResult.FilesWritten.Count} JSON files written to '{outputDataPath}'");
        foreach (var file in exportResult.FilesWritten)
        {
            var fi = new FileInfo(file);
            Console.WriteLine($"    {fi.Name} ({fi.Length} bytes)");
        }

        // Step 5: Generate C# code
        Console.WriteLine("\nGenerating C# code...");
        var outputGenPath = Path.Combine(_outputDir, _genDir);
        Directory.CreateDirectory(outputGenPath);

        var classGenerator = new CSharpClassGenerator(_namespace);
        var tablesGenerator = new TablesGenerator(_namespace);
        int genFileCount = 0;

        // Generate per-table files
        foreach (var tableDef in schemaResult.Document.Tables)
        {
            var code = classGenerator.Generate(tableDef, schemaResult.Enums);
            var fileName = $"{CSharpClassGenerator.GetTableClassName(tableDef.Name)}.cs";
            var filePath = Path.Combine(outputGenPath, fileName);
            File.WriteAllText(filePath, code);
            genFileCount++;
            Console.WriteLine($"  {fileName}");
        }

        // Generate Tables.cs (with custom type converters)
        var tablesCode = tablesGenerator.Generate(schemaResult.Document.Tables, schemaResult.Enums,
            schemaResult.Document.CustomTypes);
        var tablesFilePath = Path.Combine(outputGenPath, "Tables.cs");
        File.WriteAllText(tablesFilePath, tablesCode);
        genFileCount++;
        Console.WriteLine("  Tables.cs");

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
