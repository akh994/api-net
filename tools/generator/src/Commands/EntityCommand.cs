using System;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SkeletonApi.Generator.Commands;

public class EntityCommand : AsyncCommand<EntitySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EntitySettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[green]Generating entity from {settings.Input} ({settings.Type})...[/]");

        try
        {
            var parser = new Services.InputParser();
            var generator = new Services.GeneratorService();

            var entity = parser.Parse(settings.Input, settings.Type);

            await generator.GenerateEntityAsync(entity, settings.Output, settings.DryRun);

            if (settings.DryRun)
            {
                AnsiConsole.MarkupLine("[yellow]Dry run completed. No files were written.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[green]Generation completed successfully![/]");
            }
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}
