using System.ComponentModel;
using Spectre.Console.Cli;

namespace SkeletonApi.Generator.Commands;

public class EntitySettings : CommandSettings
{
    [CommandOption("-i|--input <PATH>")]
    [Description("Input file path (json, proto, or sql)")]
    public required string Input { get; set; }

    [CommandOption("-t|--type <TYPE>")]
    [Description("Input type (json, proto, schema)")]
    public required string Type { get; set; }

    [CommandOption("-o|--output <PATH>")]
    [Description("Output directory (project root)")]
    [DefaultValue(".")]
    public required string Output { get; set; }

    [CommandOption("--dry-run")]
    [Description("Preview generated files without writing")]
    public bool DryRun { get; set; }
}
