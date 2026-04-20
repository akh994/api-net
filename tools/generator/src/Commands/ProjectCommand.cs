using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SkeletonApi.Generator.Commands;

public class ProjectSettings : CommandSettings
{
    [CommandOption("-n|--name <NAME>")]
    [Description("Project name")]
    public string Name { get; set; } = "";

    [CommandOption("-i|--input <PATH>")]
    [Description("Input file path (json, proto, sql, yaml, or openapi)")]
    public string Input { get; set; } = "";

    [CommandOption("-t|--type <TYPE>")]
    [Description("Input type (json, proto, schema, mq, openapi, yaml, yml)")]
    public string Type { get; set; } = "proto";

    [CommandOption("--db <DB>")]
    [Description("Database provider (mysql, postgresql, sqlserver)")]
    public string DbProvider { get; set; } = "mysql";

    [CommandOption("-o|--output <PATH>")]
    [Description("Output directory")]
    public string Output { get; set; } = "";

    [CommandOption("-s|--skeleton <PATH>")]
    [Description("Path to skeleton-api-net (default: auto-detect)")]
    public string SkeletonPath { get; set; } = "";

    [CommandOption("--mq")]
    [Description("Include message queue (Consumer/Publisher)")]
    public bool IncludeMq { get; set; }


    [CommandOption("--dry-run")]
    [Description("Preview generated files without writing")]
    public bool DryRun { get; set; }

    [CommandOption("--only-domain-contracts")]
    [Description("Only generate Domain and Contracts layers")]
    public bool OnlyDomainAndContracts { get; set; }

    [CommandOption("--only-domain-mapper")]
    [Description("Only generate Domain and Mapper")]
    public bool OnlyDomainAndMapper { get; set; }

    [CommandOption("--update")]
    [Description("Update existing project without deleting it")]
    public bool Update { get; set; }

    [CommandOption("--pkg-type <TYPE>")]
    [Description("Package type for Common project (local, remote). Default: local")]
    [DefaultValue("local")]
    public string PkgType { get; set; } = "local";

    [CommandOption("--common-pkg-name <NAME>")]
    [Description("NuGet package name for Common project (if pkg-type is remote)")]
    [DefaultValue("SkeletonApi.Common")]
    public string CommonPkgName { get; set; } = "SkeletonApi.Common";

    [CommandOption("--common-pkg-version <VERSION>")]
    [Description("NuGet package version for Common project (if pkg-type is remote)")]
    [DefaultValue("*")]
    public string CommonPkgVersion { get; set; } = "*";
}

public class ProjectCommand : AsyncCommand<ProjectSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ProjectSettings settings, CancellationToken cancellationToken)
    {
        // Detect MQ-only project (alias to new-consumer command)
        if (settings.Type?.ToLower() == "mq")
        {
            AnsiConsole.MarkupLine("[yellow]Note: Using 'project --type mq' is an alias for 'new-consumer' command[/]");
            AnsiConsole.MarkupLine("[yellow]      For new projects, consider using: new-consumer --name {0} --input {1} --output {2}[/]",
                settings.Name, settings.Input, settings.Output);

            // Find skeleton path
            var skeletonPath = settings.SkeletonPath;
            if (string.IsNullOrEmpty(skeletonPath))
            {
                skeletonPath = FindSkeletonPath();
            }

            if (string.IsNullOrEmpty(skeletonPath) || !Directory.Exists(skeletonPath))
            {
                AnsiConsole.MarkupLine($"[red]Error: Could not find skeleton-api-net. Use --skeleton to specify path.[/]");
                return 1;
            }

            // Redirect to NewConsumerProjectGenerator
            var codeInjector = new Services.CodeInjector(settings.DryRun);
            var generator = new Services.NewConsumerProjectGenerator(skeletonPath, codeInjector);
            await generator.GenerateAsync(
                settings.Input,
                settings.Name,
                settings.Output,
                settings.DryRun,
                settings.Update);
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Generating new project: {settings.Name}[/]");
        AnsiConsole.MarkupLine($"[blue]Input: {settings.Input} (type: {settings.Type})[/]");
        AnsiConsole.MarkupLine($"[blue]Output: {settings.Output}[/]");
        AnsiConsole.MarkupLine($"[blue]Include MQ: {settings.IncludeMq}[/]");

        try
        {
            // Find skeleton path
            var skeletonPath = settings.SkeletonPath;
            if (string.IsNullOrEmpty(skeletonPath))
            {
                skeletonPath = FindSkeletonPath();
            }

            if (string.IsNullOrEmpty(skeletonPath) || !Directory.Exists(skeletonPath))
            {
                AnsiConsole.MarkupLine($"[red]Error: Could not find skeleton-api-net. Use --skeleton to specify path.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[blue]Skeleton source: {skeletonPath}[/]");

            // Check if input is SQL schema (multiple tables support)
            var isSqlSchema = settings.Type?.ToLower() == "schema" || settings.Type?.ToLower() == "sql";
            var codeInjector = new Services.CodeInjector(settings.DryRun);
            var testGenerator = new Services.TestGenerator();

            if (isSqlSchema)
            {
                // Parse multiple tables from SQL
                var sqlParser = new Services.SqlParser();
                var sqlContent = File.ReadAllText(settings.Input);
                var entities = sqlParser.ParseMultiple(sqlContent);

                if (entities.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[red]Error: No CREATE TABLE statements found in SQL file[/]");
                    return 1;
                }

                AnsiConsole.MarkupLine($"[blue]Found {entities.Count} tables in SQL schema[/]");
                foreach (var ent in entities)
                {
                    AnsiConsole.MarkupLine($"[blue]  - {ent.Name} ({ent.Fields.Count} fields)[/]");
                }

                // Generate project with all entities
                var generator = new Services.SmartProjectGenerator(skeletonPath, codeInjector, testGenerator,
                    settings.PkgType, settings.CommonPkgName, settings.CommonPkgVersion);
                await generator.GenerateMultipleEntitiesAsync(
                    entities,
                    settings.Name,
                    settings.Output,
                    settings.DryRun,
                    settings.IncludeMq,
                    settings.DbProvider,
                    settings.Update);

                return 0;
            }

            // Single entity parsing (existing flow)
            var parser = new Services.InputParser();
            var entity = parser.Parse(settings.Input, settings.Type);

            AnsiConsole.MarkupLine($"[blue]Entity: {entity.Name} with {entity.Fields.Count} fields, {entity.Methods.Count} methods[/]");
            AnsiConsole.MarkupLine($"[blue]Methods: {string.Join(", ", entity.Methods)}[/]");

            // Generate project using smart generator
            var singleGenerator = new Services.SmartProjectGenerator(skeletonPath, codeInjector, testGenerator,
                settings.PkgType, settings.CommonPkgName, settings.CommonPkgVersion);
            await singleGenerator.GenerateAsync(
                entity,
                settings.Name,
                settings.Output,
                settings.DryRun,
                settings.IncludeMq,
                settings.DbProvider,
                settings.OnlyDomainAndContracts,
                settings.Update,
                settings.OnlyDomainAndMapper);

            if (settings.Type.ToLower() == "rest" || settings.Type.ToLower() == "openapi" || settings.Type.ToLower() == "yaml" || settings.Type.ToLower() == "yml")
            {
                var restGenerator = new Services.RestClientGenerator(codeInjector);
                // Use entity name for service name if it's openapi
                var serviceName = settings.Type.ToLower() == "rest" ? settings.Name : entity.Name;
                await restGenerator.Generate(settings.Input, serviceName, settings.Output, settings.DryRun, settings.Type);
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private string? FindSkeletonPath()
    {
        var currentDir = AppContext.BaseDirectory;

        var possiblePaths = new[]
        {
            Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..")),
            Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", "skeleton-api-net")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..")),
            Environment.CurrentDirectory,
        };

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(Path.Combine(path, "src", "SkeletonApi")))
            {
                return path;
            }
        }

        return null;
    }
}
