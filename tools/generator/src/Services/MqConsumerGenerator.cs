using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace SkeletonApi.Generator.Services;

public class MqConsumerGenerator
{
    private readonly CodeInjector _codeInjector;

    public MqConsumerGenerator(CodeInjector codeInjector)
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
        var consumerDef = JsonSerializer.Deserialize<ConsumerDefinition>(jsonContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (consumerDef == null)
        {
            AnsiConsole.MarkupLine("[red]Error: Failed to parse MQ consumer definition[/]");
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

        // Ensure base classes exist
        EnsureBaseClasses(projectName, outputPath, dryRun);

        // Generate files
        GenerateEventModels(projectName, consumerDef.Methods, outputPath, dryRun);
        GenerateServices(projectName, consumerDef.Methods, outputPath, dryRun);
        GenerateConsumers(projectName, consumerDef.Methods, outputPath, dryRun);
        UpdateDependencyInjection(projectName, consumerDef.Methods, outputPath, dryRun);
        UpdateAppSettings(projectName, consumerDef, outputPath, dryRun);
        if (consumerDef.BrokerType.Equals("rabbitmq", StringComparison.OrdinalIgnoreCase))
        {
            EnsureRabbitMQGeneralConfig(projectName, outputPath, dryRun);
        }

        AnsiConsole.MarkupLine($"[green]✓ MQ consumer generation completed[/]");
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

    private void EnsureBaseClasses(string projectName, string outputPath, bool dryRun)
    {
        var commonMessagingDir = Path.Combine(outputPath, "src", $"{projectName}.Common", "Messaging", "Abstractions");
        var baseConsumerPath = Path.Combine(commonMessagingDir, "BaseMessageConsumer.cs");

        if (!File.Exists(baseConsumerPath))
        {
            var content = $@"using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using {projectName}.Common.Configuration;
using {projectName}.Common.Messaging;
using {projectName}.Common.Messaging.Abstractions;

namespace {projectName}.Common.Messaging;

/// <summary>
/// Base class for message consumers that run as background services
/// </summary>
public abstract class BaseMessageConsumer<TMessage> : BackgroundService
{{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly IServiceScopeFactory ScopeFactory;
    protected readonly ILogger Logger;
    protected readonly IConfiguration Configuration;
    protected readonly MessageConsumersOptions Options;
    protected readonly MessageClientFactory ClientFactory;
    protected readonly List<ISubscriptionHandler> SubscriptionHandlers = new();

    protected abstract string Topic {{ get; }}

    protected BaseMessageConsumer(
        IServiceProvider serviceProvider,
        IOptions<MessageConsumersOptions> options,
        MessageClientFactory clientFactory,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        IConfiguration configuration)
    {{
        ServiceProvider = serviceProvider;
        Options = options.Value;
        ClientFactory = clientFactory;
        ScopeFactory = scopeFactory;
        Logger = logger;
        Configuration = configuration;
    }}

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {{
        Logger.LogInformation(""Starting consumer for topic {{Topic}}"", Topic);

        try
        {{
            bool subscribed = false;

            // Iterate all configured topics to find matches for our Topic key
            foreach (var topicEntry in Options.Topics)
            {{
                var key = topicEntry.Key;
                var config = topicEntry.Value;

                // Match if Key is Topic OR configured Name is Topic
                if (key.Equals(Topic, StringComparison.OrdinalIgnoreCase) ||
                    config.Name.Equals(Topic, StringComparison.OrdinalIgnoreCase))
                {{
                    await SubscribeToTopicAsync(key, config, stoppingToken);
                    subscribed = true;
                }}
            }}

            if (!subscribed)
            {{
                Logger.LogWarning(""No configuration found for topic '{{Topic}}', Consumer will be idle."", Topic);
            }}

            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }}
        catch (OperationCanceledException)
        {{
            // Normal shutdown
        }}
        catch (Exception ex)
        {{
            Logger.LogCritical(ex, ""Fatal error in consumer for topic {{Topic}}"", Topic);
            throw;
        }}
    }}

    private async Task SubscribeToTopicAsync(string key, ConsumerTopicConfig config, CancellationToken stoppingToken)
    {{
        try
        {{
            // Merge generic config with specific config
            Dictionary<string, object> mergedConfig = new Dictionary<string, object>();
            
            // Robustly get general config from IConfiguration if Options version is empty/dummy
            var generalSection = Configuration.GetSection($""MessageConsumers:GeneralMQConfig:{{config.Type}}"");
            var genDict = generalSection.Get<Dictionary<string, object>>() ?? new Dictionary<string, object>();
            
            mergedConfig = ConfigHelper.Merge(genDict, config.MQConfig);

            var client = ClientFactory.CreateClient(config.Type, mergedConfig);

            // Determine subscription name
            var subscriptionName = !string.IsNullOrEmpty(Options.Subscription) ? Options.Subscription : ""{projectName}"";

            var subOptions = new SubscriptionOptions
            {{
                ConcurrentHandlers = 1,
                EnableDeadLetterQueue = true,
                PrefetchCount = 10,
                MessageTtlMs = 7 * 24 * 60 * 60 * 1000 // 7 days
            }};

            // Enhanced mapping from config (supports PascalCase and snake_case)
            if (mergedConfig.TryGetValue(""ConcurrentConsumers"", out var ccObj) || mergedConfig.TryGetValue(""concurrent_consumers"", out ccObj))
            {{
                if (ccObj is int cc) subOptions.ConcurrentHandlers = cc;
                else if (int.TryParse(ccObj.ToString(), out int ccParsed)) subOptions.ConcurrentHandlers = ccParsed;
            }}

            if (mergedConfig.TryGetValue(""MaxRetries"", out var mrObj) || mergedConfig.TryGetValue(""max_retries"", out mrObj))
            {{
                if (mrObj is int mr) subOptions.MaxRetries = mr;
                else if (int.TryParse(mrObj.ToString(), out int mrParsed)) subOptions.MaxRetries = mrParsed;
            }}

            if (mergedConfig.TryGetValue(""EnableDlq"", out var edObj) || mergedConfig.TryGetValue(""enable_dlq"", out edObj))
            {{
                if (edObj is bool ed) subOptions.EnableDeadLetterQueue = ed;
                else if (bool.TryParse(edObj.ToString(), out bool edParsed)) subOptions.EnableDeadLetterQueue = edParsed;
            }}

            if (mergedConfig.TryGetValue(""MessageTtl"", out var mtObj) || mergedConfig.TryGetValue(""message_ttl"", out mtObj))
            {{
                if (mtObj is int mt) subOptions.MessageTtlMs = mt * 1000;
                else if (int.TryParse(mtObj.ToString(), out int mtParsed)) subOptions.MessageTtlMs = mtParsed * 1000;
            }}

            if (mergedConfig.TryGetValue(""PrefetchCount"", out var pcObj) || mergedConfig.TryGetValue(""prefetch_count"", out pcObj))
            {{
                if (pcObj is ushort pc) subOptions.PrefetchCount = pc;
                else if (ushort.TryParse(pcObj.ToString(), out ushort pcParsed)) subOptions.PrefetchCount = pcParsed;
            }}

            var handler = await client.SubscribeAsync(
                config.Name, 
                $""{{subscriptionName}}-{{key}}"", 
                async context => await InternalHandleMessageAsync(context, stoppingToken),
                subOptions,
                stoppingToken);

            SubscriptionHandlers.Add(handler);
            Logger.LogInformation(
                ""Subscribed to topic '{{Topic}}' using {{Type}} (Key: {{Key}})"",
                config.Name,
                config.Type,
                key);
        }}
        catch (Exception ex)
        {{
            Logger.LogError(ex, ""Failed to subscribe to topic key {{Key}}"", key);
        }}
    }}

    private async Task InternalHandleMessageAsync(MessageContext context, CancellationToken stoppingToken)
    {{
        try
        {{
            using var scope = ScopeFactory.CreateScope();

            // Extract claims from message headers and set to UserContext
            var userContext = scope.ServiceProvider.GetRequiredService<SkeletonApi.Common.Interfaces.IUserContext>();
            userContext.ExtractClaims(context.Metadata);

            var options = new JsonSerializerOptions {{ PropertyNameCaseInsensitive = true }};
            var message = JsonSerializer.Deserialize<TMessage>(context.Body, options);
            if (message != null)
            {{
                await ProcessMessageAsync(message, scope, stoppingToken);
            }}
        }}
        catch (Exception ex)
        {{
            Logger.LogError(ex, ""Error processing message from {{Topic}}"", Topic);
            throw;
        }}
    }}

    public override async Task StopAsync(CancellationToken cancellationToken)
    {{
        foreach (var handler in SubscriptionHandlers)
        {{
            await handler.StopAsync(cancellationToken);
        }}
        await base.StopAsync(cancellationToken);
    }}

    protected abstract Task ProcessMessageAsync(TMessage message, IServiceScope scope, CancellationToken cancellationToken);
}}";
            if (!dryRun)
            {
                if (!Directory.Exists(commonMessagingDir))
                {
                    Directory.CreateDirectory(commonMessagingDir);
                }
                File.WriteAllText(baseConsumerPath, content);
                AnsiConsole.MarkupLine($"[green]✓ Generated missing base class: {baseConsumerPath}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[dim]Would generate missing base class: {baseConsumerPath}[/]");
            }
        }
    }

    private void GenerateEventModels(string projectName, List<ConsumerMethod> methods, string outputPath, bool dryRun)
    {
        foreach (var method in methods)
        {
            var eventName = string.IsNullOrEmpty(method.EventName) ? method.Name : method.EventName;
            if (method.Payload != null && method.Payload.Any())
            {
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

    private void GenerateServices(string projectName, List<ConsumerMethod> methods, string outputPath, bool dryRun)
    {
        foreach (var method in methods)
        {
            var eventName = string.IsNullOrEmpty(method.EventName) ? method.Name : method.EventName;
            var interfacePath = Path.Combine(outputPath, "src", $"{projectName}.Application", "Interfaces", $"I{eventName}EventService.cs");
            var servicePath = Path.Combine(outputPath, "src", $"{projectName}.Application", "Services", $"{eventName}EventService.cs");

            // Interface
            var sbInt = new StringBuilder();
            sbInt.AppendLine($"using {projectName}.Application.Models.Events;");
            sbInt.AppendLine();
            sbInt.AppendLine($"namespace {projectName}.Application.Interfaces;");
            sbInt.AppendLine();
            sbInt.AppendLine($"public interface I{eventName}EventService");
            sbInt.AppendLine("{");
            sbInt.AppendLine($"    Task Process{eventName}Async({eventName} @event, CancellationToken cancellationToken = default);");
            sbInt.AppendLine("}");
            WriteFile(interfacePath, sbInt.ToString(), dryRun, projectName);

            // Service
            var sbSvc = new StringBuilder();
            sbSvc.AppendLine("using Microsoft.Extensions.Logging;");
            sbSvc.AppendLine($"using {projectName}.Application.Interfaces;");
            sbSvc.AppendLine($"using {projectName}.Application.Models.Events;");
            sbSvc.AppendLine();
            sbSvc.AppendLine($"namespace {projectName}.Application.Services;");
            sbSvc.AppendLine();
            sbSvc.AppendLine($"public class {eventName}EventService : I{eventName}EventService");
            sbSvc.AppendLine("{");
            sbSvc.AppendLine($"    private readonly ILogger<{eventName}EventService> _logger;");
            sbSvc.AppendLine();
            sbSvc.AppendLine($"    public {eventName}EventService(ILogger<{eventName}EventService> logger)");
            sbSvc.AppendLine("    {");
            sbSvc.AppendLine("        _logger = logger;");
            sbSvc.AppendLine("    }");
            sbSvc.AppendLine();
            sbSvc.AppendLine($"    public async Task Process{eventName}Async({eventName} @event, CancellationToken cancellationToken = default)");
            sbSvc.AppendLine("    {");
            sbSvc.AppendLine($"        _logger.LogInformation(\"Processing {eventName} event\");");
            sbSvc.AppendLine("        await Task.CompletedTask;");
            sbSvc.AppendLine("    }");
            sbSvc.AppendLine("}");
            WriteFile(servicePath, sbSvc.ToString(), dryRun, projectName);
        }
    }

    private void GenerateConsumers(string projectName, List<ConsumerMethod> methods, string outputPath, bool dryRun)
    {
        foreach (var method in methods)
        {
            var eventName = string.IsNullOrEmpty(method.EventName) ? method.Name : method.EventName;
            var consumerPath = Path.Combine(outputPath, "src", projectName, "Consumers", $"{eventName}Consumer.cs");

            var sb = new StringBuilder();
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine("using Microsoft.Extensions.Logging;");
            sb.AppendLine("using Microsoft.Extensions.Options;");
            sb.AppendLine($"using {projectName}.Application.Interfaces;");
            sb.AppendLine($"using {projectName}.Application.Models.Events;");
            sb.AppendLine($"using {projectName}.Common.Configuration;");
            sb.AppendLine($"using {projectName}.Common.Messaging;");
            sb.AppendLine($"using {projectName}.Common.Messaging.Abstractions;");
            sb.AppendLine();
            sb.AppendLine($"namespace {projectName}.Consumers;");
            sb.AppendLine();
            sb.AppendLine($"public class {eventName}Consumer : BaseMessageConsumer<{eventName}>");
            sb.AppendLine("{");
            sb.AppendLine($"    public {eventName}Consumer(");
            sb.AppendLine("        IServiceProvider serviceProvider,");
            sb.AppendLine("        IOptions<MessageConsumersOptions> options,");
            sb.AppendLine("        MessageClientFactory clientFactory,");
            sb.AppendLine("        IServiceScopeFactory scopeFactory,");
            sb.AppendLine($"        ILogger<{eventName}Consumer> logger,");
            sb.AppendLine("        IConfiguration configuration)");
            sb.AppendLine("        : base(serviceProvider, options, clientFactory, scopeFactory, logger, configuration)");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    protected override string Topic => \"{method.Topic}\";");
            sb.AppendLine();
            sb.AppendLine($"    protected override async Task ProcessMessageAsync({eventName} message, IServiceScope scope, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var service = scope.ServiceProvider.GetRequiredService<I{eventName}EventService>();");
            sb.AppendLine($"        await service.Process{eventName}Async(message, cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            WriteFile(consumerPath, sb.ToString(), dryRun, projectName);
        }
    }

    private void UpdateDependencyInjection(string projectName, List<ConsumerMethod> methods, string outputPath, bool dryRun)
    {
        var diPath = Path.Combine(outputPath, "src", projectName, "Extensions", "DependencyInjection.cs");

        // Inject Required Usings
        _codeInjector.InjectUsing(diPath, $"{projectName}.Common.Messaging");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Common.Messaging.Abstractions");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Common.Configuration");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Application.Interfaces");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Application.Services");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Consumers");

        var sbApp = new StringBuilder();
        var sbInfra = new StringBuilder();

        foreach (var method in methods)
        {
            var eventName = string.IsNullOrEmpty(method.EventName) ? method.Name : method.EventName;
            sbApp.AppendLine($"        services.AddScoped<I{eventName}EventService, {eventName}EventService>();");
            sbInfra.AppendLine($"        services.AddHostedService<{eventName}Consumer>();");
        }

        // Register Messaging Infrastructure (Idempotent injection handled by CodeInjector)
        sbInfra.AppendLine($"        services.AddSingleton<{projectName}.Common.Messaging.MessagingConnectionManager>();");
        sbInfra.AppendLine($"        services.AddSingleton<{projectName}.Common.Messaging.MessageClientFactory>();");
        sbInfra.AppendLine($"        services.AddSingleton<{projectName}.Common.Messaging.MessageClientProvider>();");

        _codeInjector.InjectDependency(diPath, sbApp.ToString(), "AddApplication");
        _codeInjector.InjectDependency(diPath, sbInfra.ToString(), "AddInfrastructure");
    }

    private void UpdateAppSettings(string projectName, ConsumerDefinition consumerDef, string outputPath, bool dryRun)
    {
        var projectDir = Path.Combine(outputPath, "src", projectName);
        var brokerType = consumerDef.BrokerType; // Read from JSON instead of hardcoding

        foreach (var method in consumerDef.Methods)
        {
            var topicKey = method.Name;
            var topicName = method.Topic;

            // Updated structure for topic-centric configuration
            var section = $"\n        \"{topicKey}\": {{\n            \"Type\": \"{brokerType}\",\n            \"Name\": \"{topicName}\"\n        }},";
            _codeInjector.InjectAppSettingToAllConfigs(projectDir, "MessageConsumers:Topics", section);
        }
    }

    private string GetEntityName(string eventName)
    {
        if (eventName.EndsWith("Event")) return eventName.Replace("Event", "");
        if (eventName.EndsWith("Created")) return eventName.Replace("Created", "");
        if (eventName.EndsWith("Updated")) return eventName.Replace("Updated", "");
        if (eventName.EndsWith("Deleted")) return eventName.Replace("Deleted", "");
        return eventName;
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

public class ConsumerDefinition
{
    [System.Text.Json.Serialization.JsonPropertyName("broker_type")]
    public string BrokerType { get; set; } = "rabbitmq"; // Default to rabbitmq for backward compatibility
    public List<ConsumerMethod> Methods { get; set; } = new();
}

public class ConsumerMethod
{
    public string Name { get; set; } = "";
    public string Topic { get; set; } = "";
    public string Subscription { get; set; } = "";
    public string EventName { get; set; } = "";
    public string BrokerKey { get; set; } = "default";
    public Dictionary<string, string> Payload { get; set; } = new();
}
