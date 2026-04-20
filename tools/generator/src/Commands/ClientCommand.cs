using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SkeletonApi.Generator.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SkeletonApi.Generator.Commands;

public class ClientSettings : CommandSettings
{
    [CommandOption("-i|--input <PATH>")]
    [Description("Input file path (proto for gRPC, json/yaml/openapi for REST)")]
    public string Input { get; set; } = "";

    [CommandOption("-t|--type <TYPE>")]
    [Description("Client type (grpc, rest, openapi)")]
    public string Type { get; set; } = "grpc";

    [CommandOption("-n|--name <NAME>")]
    [Description("Service name")]
    public string Name { get; set; } = "";

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

public class ClientCommand : AsyncCommand<ClientSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ClientSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[green]Generating {settings.Type} client from {settings.Input}...[/]");

        var codeInjector = new CodeInjector(settings.DryRun);
        codeInjector.PkgType = settings.PkgType;
        codeInjector.CommonPkgName = settings.CommonPkgName;
        codeInjector.CommonPkgVersion = settings.CommonPkgVersion;

        if (settings.Type.ToLower() == "grpc")
        {
            var generator = new GrpcClientGenerator(codeInjector);
            generator.Generate(settings.Input, settings.Name, settings.Output, settings.DryRun);
        }
        else if (settings.Type.ToLower() == "rest" || settings.Type.ToLower() == "openapi")
        {
            var generator = new RestClientGenerator(codeInjector);
            await generator.Generate(settings.Input, settings.Name, settings.Output, settings.DryRun, settings.Type);
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Unknown client type: {settings.Type}. Use 'grpc', 'rest', or 'openapi'[/]");
            return 1;
        }

        return 0;
    }
}
