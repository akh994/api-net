using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkeletonApi.Generator.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SkeletonApi.Generator.Commands;

public class MqSettings : CommandSettings
{
    [CommandOption("-i|--input <PATH>")]
    [Description("Input file path (mq_publisher.json or mq_subscriber.json)")]
    public string Input { get; set; } = "";

    [CommandOption("-o|--output <PATH>")]
    [Description("Output directory (project root)")]
    public string Output { get; set; } = ".";

    [CommandOption("--dry-run")]
    [Description("Preview generated files without writing")]
    public bool DryRun { get; set; }

    [CommandOption("--pkg-type <TYPE>")]
    [Description("Package type for Common logic (local, remote)")]
    public string PkgType { get; set; } = "local";

    [CommandOption("--common-pkg-name <NAME>")]
    [Description("NuGet package name for Common logic")]
    public string CommonPkgName { get; set; } = "SkeletonApi.Common";

    [CommandOption("--common-pkg-version <VERSION>")]
    [Description("NuGet package version for Common logic")]
    public string CommonPkgVersion { get; set; } = "*";
}

public class MqCommand : AsyncCommand<MqSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, MqSettings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.Input))
        {
            AnsiConsole.MarkupLine($"[red]Error: Input file not found: {settings.Input}[/]");
            return Task.FromResult(1);
        }

        var jsonContent = File.ReadAllText(settings.Input);

        // Simple check for subscription field to determine if it's a consumer definition
        bool isConsumer = jsonContent.Contains("\"subscription\"");

        var codeInjector = new CodeInjector(settings.DryRun);
        codeInjector.PkgType = settings.PkgType;
        codeInjector.CommonPkgName = settings.CommonPkgName;
        codeInjector.CommonPkgVersion = settings.CommonPkgVersion;

        if (isConsumer)
        {
            AnsiConsole.MarkupLine($"[green]Generating MQ consumer from {settings.Input}...[/]");
            var generator = new MqConsumerGenerator(codeInjector);
            generator.Generate(settings.Input, settings.Output, settings.DryRun);
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Generating MQ publisher from {settings.Input}...[/]");
            var generator = new MqPublisherGenerator(codeInjector);
            generator.Generate(settings.Input, settings.Output, settings.DryRun);
        }

        return Task.FromResult(0);
    }
}
