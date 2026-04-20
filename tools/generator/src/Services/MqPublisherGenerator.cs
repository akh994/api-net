using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace SkeletonApi.Generator.Services;

public class MqPublisherGenerator
{
    private readonly CodeInjector _codeInjector;

    public MqPublisherGenerator(CodeInjector codeInjector)
    {
        _codeInjector = codeInjector;
    }

    public void Generate(string inputPath, string outputPath, bool dryRun)
    {
        if (!File.Exists(inputPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Input file not found: {inputPath}[/]");
            return;
        }

        var jsonContent = File.ReadAllText(inputPath);
        var publisherDef = JsonSerializer.Deserialize<PublisherDefinition>(jsonContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (publisherDef == null)
        {
            AnsiConsole.MarkupLine("[red]Error: Failed to parse MQ publisher definition[/]");
            return;
        }

        // Determine project name from output path
        var projectName = _codeInjector.DetectProjectName(outputPath);
        if (string.IsNullOrEmpty(projectName))
        {
            AnsiConsole.MarkupLine("[red]Error: Could not detect project name from output path[/]");
            return;
        }

        // Auto-detect if project uses remote common pkg
        _codeInjector.AutoDetectRemoteConfig(outputPath);

        AnsiConsole.MarkupLine($"[cyan]Detected project: {projectName}[/]");

        // Generate files
        GenerateEventModels(projectName, publisherDef.Methods, outputPath, dryRun);
        GenerateInterface(projectName, publisherDef, outputPath, dryRun);
        GeneratePublisher(projectName, publisherDef.PublisherType, publisherDef, outputPath, dryRun);
        UpdateDependencyInjection(projectName, publisherDef, outputPath, dryRun);
        UpdateAppSettings(projectName, publisherDef.PublisherType, publisherDef.Methods, outputPath, dryRun);
        if (publisherDef.PublisherType.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
        {
            EnsureRabbitMQGeneralConfig(projectName, outputPath, dryRun);
        }

        AnsiConsole.MarkupLine($"[green]✓ MQ publisher generation completed[/]");
    }

    private void EnsureRabbitMQGeneralConfig(string projectName, string outputPath, bool dryRun)
    {
        var projectDir = Path.Combine(outputPath, "src", projectName);
        var configFields = new Dictionary<string, string>
        {
            { "queue_auto_delete", "false" },
            { "queue_durable", "true" },
            { "exchange_durable", "true" }
        };

        foreach (var field in configFields)
        {
            var section = $"\n        \"{field.Key}\": {field.Value},";
            // Inject to both Publishers and Consumers just in case
            _codeInjector.InjectAppSettingToAllConfigs(projectDir, "MessagePublishers:GeneralMQConfig:rabbitmq", section);
            _codeInjector.InjectAppSettingToAllConfigs(projectDir, "MessageConsumers:GeneralMQConfig:rabbitmq", section);
        }
    }

    private void GenerateEventModels(string projectName, List<PublisherMethod> methods, string outputPath, bool dryRun)
    {
        foreach (var method in methods)
        {
            if (method.Payload != null && method.Payload.Any())
            {
                var eventName = string.IsNullOrEmpty(method.EventName) ? method.Name : method.EventName;
                var modelPath = Path.Combine(outputPath, "src", $"{projectName}.Application", "Models", "Events", $"{eventName}.cs");

                var sb = new StringBuilder();
                sb.AppendLine("using System.Text.Json.Serialization;");
                sb.AppendLine();
                sb.AppendLine($"namespace {projectName}.Application.Models.Events;");
                sb.AppendLine();
                sb.AppendLine($"public class {eventName}");
                sb.AppendLine("{");

                foreach (var field in method.Payload)
                {
                    var propName = ToPascalCase(field.Key);
                    var propType = MapJsonTypeToCSharp(field.Value);

                    sb.AppendLine($"    [JsonPropertyName(\"{field.Key}\")]");
                    sb.AppendLine($"    public {propType} {propName} {{ get; set; }} = default!;");
                    sb.AppendLine();
                }

                sb.AppendLine("}");

                WriteFile(modelPath, sb.ToString(), dryRun, projectName);
            }
        }
    }

    private string MapJsonTypeToCSharp(string type)
    {
        return type.ToLower() switch
        {
            "string" => "string",
            "number" or "double" or "float" => "double",
            "int" or "integer" or "int32" => "int",
            "long" or "int64" => "long",
            "bool" or "boolean" => "bool",
            "datetime" => "DateTime",
            _ => "object"
        };
    }

    private void GenerateInterface(string projectName, PublisherDefinition def, string outputPath, bool dryRun)
    {
        var interfacePath = Path.Combine(outputPath, "src", $"{projectName}.Application", "Interfaces", "IMessagePublisher.cs");

        if (File.Exists(interfacePath))
        {
            foreach (var method in def.Methods)
            {
                var eventName = string.IsNullOrEmpty(method.EventName) ? method.Name : method.EventName;
                var methodCode = $"    Task {method.Name}Async({eventName} @event, CancellationToken cancellationToken = default);";
                _codeInjector.InjectInterfaceMethod(interfacePath, methodCode);
            }
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"using {projectName}.Application.Models.Events;");
        sb.AppendLine();
        sb.AppendLine($"namespace {projectName}.Application.Interfaces;");
        sb.AppendLine();
        sb.AppendLine("public interface IMessagePublisher");
        sb.AppendLine("{");

        foreach (var method in def.Methods)
        {
            var eventName = string.IsNullOrEmpty(method.EventName) ? method.Name : method.EventName;
            sb.AppendLine($"    Task {method.Name}Async({eventName} @event, CancellationToken cancellationToken = default);");
        }

        sb.AppendLine("}");

        WriteFile(interfacePath, sb.ToString(), dryRun, projectName);
    }

    private void GeneratePublisher(string projectName, string defPublisherType, PublisherDefinition def, string outputPath, bool dryRun)
    {
        var className = "MessagePublisher";
        var publisherPath = Path.Combine(outputPath, "src", $"{projectName}.Infrastructure", "Messaging", $"{className}.cs");

        if (File.Exists(publisherPath))
        {
            foreach (var method in def.Methods)
            {
                var eventName = string.IsNullOrEmpty(method.EventName) ? method.Name : method.EventName;
                var sbMethod = new StringBuilder();
                sbMethod.AppendLine($"    public async Task {method.Name}Async({eventName} @event, CancellationToken cancellationToken = default)");
                sbMethod.AppendLine("    {");
                sbMethod.AppendLine($"        _logger.LogInformation(\"Publishing {eventName} event\");");
                sbMethod.AppendLine("        try");
                sbMethod.AppendLine("        {");
                sbMethod.AppendLine("            var json = JsonSerializer.Serialize(@event);");
                sbMethod.AppendLine("            var body = Encoding.UTF8.GetBytes(json);");
                sbMethod.AppendLine($"            var client = _messageClientProvider.GetClientForTopic(\"{method.Name}\");");
                sbMethod.AppendLine($"            await client.PublishAsync(\"{method.Topic}\", body, cancellationToken);");
                sbMethod.AppendLine("        }");
                sbMethod.AppendLine("        catch (Exception ex)");
                sbMethod.AppendLine("        {");
                sbMethod.AppendLine($"            _logger.LogError(ex, \"Error publishing {eventName} event\");");
                sbMethod.AppendLine("            throw;");
                sbMethod.AppendLine("        }");
                sbMethod.AppendLine("    }");

                _codeInjector.InjectClassMethod(publisherPath, sbMethod.ToString());
            }
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine($"using {projectName}.Application.Interfaces;");
        sb.AppendLine($"using {projectName}.Application.Models.Events;");
        sb.AppendLine($"using {projectName}.Common.Messaging;");
        sb.AppendLine($"using {projectName}.Common.Messaging.Abstractions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {projectName}.Infrastructure.Messaging;");
        sb.AppendLine();
        sb.AppendLine($"public class {className} : IMessagePublisher");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly MessageClientProvider _messageClientProvider;");
        sb.AppendLine($"    private readonly ILogger<{className}> _logger;");
        sb.AppendLine();
        sb.AppendLine($"    public {className}(MessageClientProvider messageClientProvider, ILogger<{className}> logger)");
        sb.AppendLine("    {");
        sb.AppendLine("        _messageClientProvider = messageClientProvider;");
        sb.AppendLine("        _logger = logger;");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var method in def.Methods)
        {
            var eventName = string.IsNullOrEmpty(method.EventName) ? method.Name : method.EventName;
            sb.AppendLine($"    public async Task {method.Name}Async({eventName} @event, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        _logger.LogInformation(\"Publishing {eventName} event\");");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var json = JsonSerializer.Serialize(@event);");
            sb.AppendLine("            var body = Encoding.UTF8.GetBytes(json);");
            sb.AppendLine($"            var client = _messageClientProvider.GetClientForTopic(\"{method.Name}\");");
            sb.AppendLine($"            await client.PublishAsync(\"{method.Topic}\", body, cancellationToken);");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _logger.LogError(ex, \"Error publishing {eventName} event\");");
            sb.AppendLine("            throw;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        WriteFile(publisherPath, sb.ToString(), dryRun, projectName);
    }

    private void UpdateDependencyInjection(string projectName, PublisherDefinition def, string outputPath, bool dryRun)
    {
        var diPath = Path.Combine(outputPath, "src", projectName, "Extensions", "DependencyInjection.cs");

        // Inject Required Usings
        _codeInjector.InjectUsing(diPath, $"{projectName}.Common.Messaging");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Common.Messaging.Abstractions");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Common.Configuration");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Application.Interfaces");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Infrastructure.Messaging");

        // Register the specific IMessagePublisher implementation
        var registration = $"        services.AddSingleton<IMessagePublisher, {projectName}.Infrastructure.Messaging.MessagePublisher>();";
        _codeInjector.InjectDependency(diPath, registration, "AddInfrastructure");

        // Register Messaging Infrastructure (Idempotent injection handled by CodeInjector)
        var infraRegistration = new StringBuilder();
        infraRegistration.AppendLine($"        services.AddSingleton<{projectName}.Common.Messaging.MessagingConnectionManager>();");
        infraRegistration.AppendLine($"        services.AddSingleton<{projectName}.Common.Messaging.MessageClientFactory>();");
        infraRegistration.AppendLine($"        services.AddSingleton<{projectName}.Common.Messaging.MessageClientProvider>();");
        _codeInjector.InjectDependency(diPath, infraRegistration.ToString(), "AddInfrastructure");
    }

    private void UpdateAppSettings(string projectName, string brokerType, List<PublisherMethod> methods, string outputPath, bool dryRun)
    {
        var projectDir = Path.Combine(outputPath, "src", projectName);
        foreach (var method in methods)
        {
            var topicKey = method.Name;
            var topicName = method.Topic;

            // Updated structure for topic-centric configuration
            var section = $"\n        \"{topicKey}\": {{\n            \"Type\": \"{brokerType.ToLower()}\",\n            \"Name\": \"{topicName}\"\n        }},";
            _codeInjector.InjectAppSettingToAllConfigs(projectDir, "MessagePublishers:Topics", section);
        }
    }

    private string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var parts = input.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpper(p[0]) + p.Substring(1)));
    }

    private void WriteFile(string path, string content, bool dryRun, string projectName)
    {
        if (dryRun)
        {
            AnsiConsole.MarkupLine($"[dim]Would write: {path}[/]");
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var updatedContent = _codeInjector.ReplacePlaceholders(content, projectName);
        File.WriteAllText(path, updatedContent);
        AnsiConsole.MarkupLine($"[green]✓ Generated: {path}[/]");
    }
}

// Model classes for parsing mq_publisher.json
public class PublisherDefinition
{
    [System.Text.Json.Serialization.JsonPropertyName("broker_type")]
    public string PublisherType { get; set; } = "RabbitMQ";
    public List<PublisherMethod> Methods { get; set; } = new();
}

public class PublisherMethod
{
    public string Name { get; set; } = "";
    public string Topic { get; set; } = "";
    public string EventName { get; set; } = "";
    public string BrokerKey { get; set; } = "default";
    public Dictionary<string, string> Payload { get; set; } = new();
}
