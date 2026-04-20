using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SkeletonApi.Generator.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SkeletonApi.Generator.Commands;

public class NewConsumerSettings : CommandSettings
{
    [CommandOption("-n|--name <NAME>")]
    [Description("Project name (e.g. OrderConsumer)")]
    public string Name { get; set; } = "";

    [CommandOption("-i|--input <PATH>")]
    [Description("Input file path (mq_subscriber.json)")]
    public string Input { get; set; } = "";

    [CommandOption("-o|--output <PATH>")]
    [Description("Output directory (parent folder)")]
    public string Output { get; set; } = ".";

    [CommandOption("-s|--skeleton <PATH>")]
    [Description("Path to skeleton-api-net (default: auto-detect)")]
    public string SkeletonPath { get; set; } = "";

    [CommandOption("--dry-run")]
    [Description("Preview generated files without writing")]
    public bool DryRun { get; set; }

    [CommandOption("--update")]
    [Description("Update existing project")]
    public bool Update { get; set; }
}

public class NewConsumerCommand : AsyncCommand<NewConsumerSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, NewConsumerSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(settings.Name))
            {
                AnsiConsole.MarkupLine("[red]Error: Project name is required (-n)[/]");
                return 1;
            }

            if (string.IsNullOrEmpty(settings.Input))
            {
                AnsiConsole.MarkupLine("[red]Error: Input file is required (-i)[/]");
                return 1;
            }

            if (!File.Exists(settings.Input))
            {
                AnsiConsole.MarkupLine($"[red]Error: Input file not found: {settings.Input}[/]");
                return 1;
            }

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
            Path.GetFullPath(Environment.CurrentDirectory) // In case we are running from root
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
