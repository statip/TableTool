using TableTool.Cli.Commands;

namespace TableTool.Cli;

/// <summary>Entry point for the TableTool CLI.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var schemaPath = "schema/types.yaml";   // 可选，没有也行
            var excelDir = "excel/";
            var outputDir = "output/";
            var dataDir = "data";
            var genDir = "gen";
            var ns = "GameConfig";

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--schema":
                    case "-s":
                        if (++i < args.Length) schemaPath = args[i];
                        break;
                    case "--excel":
                    case "-e":
                        if (++i < args.Length) excelDir = args[i];
                        break;
                    case "--output":
                    case "-o":
                        if (++i < args.Length) outputDir = args[i];
                        break;
                    case "--data":
                    case "-d":
                        if (++i < args.Length) dataDir = args[i];
                        break;
                    case "--gen":
                    case "-g":
                        if (++i < args.Length) genDir = args[i];
                        break;
                    case "--namespace":
                    case "-n":
                        if (++i < args.Length) ns = args[i];
                        break;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        return 0;
                    case "--version":
                    case "-v":
                        PrintVersion();
                        return 0;
                }
            }

            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            var command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "build":
                    return new BuildCommand(schemaPath, excelDir, outputDir, dataDir, genDir, ns).Execute();

                case "sample":
                    SampleBuilder.Generate(Path.GetFullPath(excelDir));
                    return 0;

                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FATAL] Unhandled exception: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("TableTool - Excel to JSON/C# configuration data tool");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  TableTool.Cli <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build           Run the full build pipeline (excel → validate → export → codegen)");
        Console.WriteLine("  sample          Generate sample Excel files for testing");
        Console.WriteLine();
        Console.WriteLine("Build Options:");
        Console.WriteLine("  --excel, -e <dir>       Input directory for .xlsx files    [default: excel/]");
        Console.WriteLine("  --schema, -s <path>     Types definition file (optional)   [default: schema/types.yaml]");
        Console.WriteLine("  --output, -o <dir>      Output directory                   [default: output/]");
        Console.WriteLine("  --data, -d <dir>        JSON data subdirectory             [default: data]");
        Console.WriteLine("  --gen, -g <dir>         C# code subdirectory               [default: gen]");
        Console.WriteLine("  --namespace, -n <name>  C# namespace for generated code    [default: GameConfig]");
        Console.WriteLine("  --help, -h              Show help");
        Console.WriteLine("  --version, -v           Show version");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  TableTool.Cli sample");
        Console.WriteLine("  TableTool.Cli build                                       (Excel 自描述模式)");
        Console.WriteLine("  TableTool.Cli build --schema schema/types.yaml            (带类型定义)");
        Console.WriteLine("  TableTool.Cli build --excel xlsx/ --output ../GameProject/Config");
    }

    private static void PrintVersion()
    {
        var version = typeof(Program).Assembly.GetName().Version;
        Console.WriteLine($"TableTool.Cli {version?.ToString() ?? "1.0.0"}");
    }
}
