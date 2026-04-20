using System.IO;
using System.Threading.Tasks;
using Scriban;
using SkeletonApi.Generator.Models;
using Spectre.Console;

namespace SkeletonApi.Generator.Services;

public class GeneratorService
{
    public async Task GenerateEntityAsync(EntityDefinition entity, string outputDir, bool dryRun)
    {
        // Scriban uses snake_case for C# properties: Name -> name, TableName -> table_name
        var domainTemplate = @"namespace SkeletonApi.Domain.Entities;

public class {{ name }}
{
{{~ for field in fields ~}}
    public {{ field.type }} {{ field.name }} { get; set; }
{{~ end ~}}
}
";

        var template = Template.Parse(domainTemplate);
        var result = await template.RenderAsync(entity);

        var outputPath = Path.Combine(outputDir, "src", "SkeletonApi.Domain", "Entities", $"{entity.Name}.cs");

        if (dryRun)
        {
            AnsiConsole.MarkupLine($"[yellow]Would write to: {outputPath}[/]");
            AnsiConsole.Write(new Panel(result).Header("Generated Content"));
        }
        else
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir!);
            }

            await File.WriteAllTextAsync(outputPath, result);
            AnsiConsole.MarkupLine($"[green]Created: {outputPath}[/]");
        }
    }
}
