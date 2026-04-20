using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkeletonApi.Generator.Models;
using Spectre.Console;

namespace SkeletonApi.Generator.Services;

public class RestClientGenerator
{
    private readonly CodeInjector _codeInjector;

    public RestClientGenerator(CodeInjector codeInjector)
    {
        _codeInjector = codeInjector;
    }

    public async Task Generate(string inputPath, string serviceName, string outputPath, bool dryRun, string inputType = "json")
    {
        if (!File.Exists(inputPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Input file not found: {inputPath}[/]");
            return;
        }

        RestClientDefinition? clientDef = null;

        if (inputType.ToLower() == "openapi" || inputType.ToLower() == "yaml" || inputType.ToLower() == "yml" || inputPath.EndsWith(".yaml") || inputPath.EndsWith(".yml"))
        {
            var parser = new OpenApiParser();
            var entity = parser.Parse(File.ReadAllText(inputPath));
            clientDef = ConvertEntityToClientDef(entity);
        }
        else
        {
            var jsonContent = File.ReadAllText(inputPath);
            clientDef = JsonSerializer.Deserialize<RestClientDefinition>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        if (clientDef == null)
        {
            AnsiConsole.MarkupLine("[red]Error: Failed to parse REST client definition[/]");
            return;
        }

        // Use service name from JSON if not provided via CLI
        var actualServiceName = string.IsNullOrEmpty(serviceName) ? clientDef.ServiceName : serviceName;
        if (string.IsNullOrEmpty(actualServiceName))
        {
            AnsiConsole.MarkupLine("[red]Error: Service name not specified in JSON or CLI[/]");
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
        AnsiConsole.MarkupLine($"[cyan]Service: {actualServiceName}[/]");
        AnsiConsole.MarkupLine($"[cyan]Methods: {clientDef.Methods.Count}[/]");

        // Generate files
        if (inputType.ToLower() == "openapi" || inputType.ToLower() == "yaml" || inputType.ToLower() == "yml" || inputPath.EndsWith(".yaml") || inputPath.EndsWith(".yml"))
        {
            var parser = new OpenApiParser();
            var entity = parser.Parse(File.ReadAllText(inputPath));
            GenerateModelsFromEntity(projectName, entity, outputPath, dryRun);
        }
        else
        {
            GenerateModels(projectName, actualServiceName, clientDef.Methods, outputPath, dryRun);
        }

        GenerateInterface(projectName, actualServiceName, clientDef.Methods, outputPath, dryRun);
        GenerateRepository(projectName, actualServiceName, clientDef.Methods, outputPath, dryRun);
        UpdateAppOptions(projectName, actualServiceName, clientDef.BaseUrlConfigKey, outputPath, dryRun);
        UpdateAppSettings(projectName, actualServiceName, clientDef.BaseUrlConfigKey, outputPath, dryRun);
        UpdateDependencyInjection(projectName, actualServiceName, clientDef.BaseUrlConfigKey, outputPath, dryRun);

        AnsiConsole.MarkupLine($"[green]✓ REST client generation completed for {actualServiceName}[/]");

        if (!dryRun)
        {
            // Format generated code
            AnsiConsole.MarkupLine("[blue]Formatting generated code...[/]");
            await RunCommandAsync("dotnet", "format --no-restore", outputPath);
        }
    }

    private async Task RunCommandAsync(string command, string args, string workingDir)
    {
        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                AnsiConsole.MarkupLine($"[red]Error running {command} {Markup.Escape(args)}: {Markup.Escape(error)}[/]");
            }
        }
    }

    private void GenerateModelsFromEntity(string projectName, EntityDefinition entity, string outputPath, bool dryRun)
    {
        foreach (var message in entity.Messages)
        {
            var modelPath = Path.Combine(outputPath, "src", $"{projectName}.Application", "Models", "Clients", $"{message.Name}.cs");

            var sb = new StringBuilder();
            sb.AppendLine("using System.Text.Json.Serialization;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine($"namespace {projectName}.Application.Models.Clients;");
            sb.AppendLine();
            sb.AppendLine($"public class {message.Name}");
            sb.AppendLine("{");

            foreach (var field in message.Fields)
            {
                var propName = ToPascalCase(field.Name);
                var propType = field.Type;
                if (field.IsRepeated && !propType.StartsWith("List<"))
                {
                    propType = $"List<{propType}>";
                }

                sb.AppendLine($"    [JsonPropertyName(\"{field.Name}\")]");
                sb.AppendLine($"    public {propType} {propName} {{ get; set; }} = default!;");
                sb.AppendLine();
            }

            sb.AppendLine("}");

            WriteFile(modelPath, sb.ToString(), dryRun, projectName);
        }
    }

    private RestClientDefinition ConvertEntityToClientDef(EntityDefinition entity)
    {
        var def = new RestClientDefinition
        {
            ServiceName = entity.ServiceName.Replace("Service", ""),
            BaseUrlConfigKey = entity.Name,
            Methods = new List<RestMethod>()
        };

        foreach (var method in entity.Methods)
        {
            var restMethod = new RestMethod
            {
                Name = method.Name,
                HttpMethod = method.HttpMethod ?? "GET",
                Path = method.HttpPath ?? "/",
                Request = new Dictionary<string, string>(),
                Response = new Dictionary<string, string>()
            };

            var reqMsg = entity.Messages.Find(m => m.Name == method.RequestType);
            if (reqMsg != null)
            {
                foreach (var field in reqMsg.Fields)
                {
                    restMethod.Request[field.Name] = field.Type + (field.IsRepeated && !field.Type.StartsWith("List<") ? "[]" : "");
                }
            }

            var resMsg = entity.Messages.Find(m => m.Name == method.ResponseType);
            if (resMsg != null)
            {
                foreach (var field in resMsg.Fields)
                {
                    restMethod.Response[field.Name] = field.Type;
                }
            }

            restMethod.RequestTypeName = method.RequestType;
            restMethod.ResponseTypeName = method.ResponseType;

            def.Methods.Add(restMethod);
        }

        return def;
    }



    private void GenerateModels(string projectName, string serviceName, List<RestMethod> methods, string outputPath, bool dryRun)
    {
        foreach (var method in methods)
        {
            if (method.Request != null && method.Request.Any())
            {
                var className = $"{method.Name}Request";
                method.RequestTypeName = className;
                GenerateModelFile(projectName, className, method.Request, outputPath, dryRun);
            }

            if (method.Response != null && method.Response.Any())
            {
                var className = $"{method.Name}Response";
                method.ResponseTypeName = className;
                GenerateModelFile(projectName, className, method.Response, outputPath, dryRun);
            }
        }
    }

    private void GenerateModelFile(string projectName, string className, Dictionary<string, string> fields, string outputPath, bool dryRun)
    {
        var modelPath = Path.Combine(outputPath, "src", $"{projectName}.Application", "Models", "Clients", $"{className}.cs");

        var sb = new StringBuilder();
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine($"namespace {projectName}.Application.Models.Clients;");
        sb.AppendLine();
        sb.AppendLine($"public class {className}");
        sb.AppendLine("{");

        foreach (var field in fields)
        {
            var propName = ToPascalCase(field.Key);
            var propType = MapType(field.Value);

            sb.AppendLine($"    [JsonPropertyName(\"{field.Key}\")]");
            sb.AppendLine($"    public {propType} {propName} {{ get; set; }} = default!;");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        WriteFile(modelPath, sb.ToString(), dryRun, projectName);
    }

    private string MapType(string type)
    {
        if (type.StartsWith("List<") || type.Contains('<')) return type;
        if (type.EndsWith("[]")) return $"List<{MapType(type.TrimEnd('[', ']'))}>";

        return type switch
        {
            "string" or "String" => "string",
            "int" or "int32" or "Int32" => "int",
            "int64" or "long" or "Int64" => "long",
            "bool" or "boolean" or "Boolean" => "bool",
            "double" or "float" or "number" or "Double" or "Float" => "double",
            "datetime" or "time.time" or "DateTime" => "DateTime",
            "guid" or "Guid" => "Guid",
            "google.protobuf.Empty" => "void",
            "google.protobuf.Timestamp" => "DateTime",
            _ => type
        };
    }

    private void GenerateInterface(string projectName, string serviceName, List<RestMethod> methods, string outputPath, bool dryRun)
    {
        var interfacePath = Path.Combine(outputPath, "src", $"{projectName}.Application", "Interfaces", $"I{serviceName}RestClient.cs");

        var sb = new StringBuilder();
        sb.AppendLine($"using {projectName}.Application.Models.Clients;");
        sb.AppendLine();
        sb.AppendLine($"namespace {projectName}.Application.Interfaces;");
        sb.AppendLine();
        sb.AppendLine($"public interface I{serviceName}RestClient");
        sb.AppendLine("{");

        foreach (var method in methods)
        {
            var reqType = MapType(method.RequestTypeName ?? "");
            var resType = MapType(method.ResponseTypeName ?? "");

            var returnType = !string.IsNullOrEmpty(resType) && resType != "void" ? $"Task<{resType}?>" : "Task";
            var paramType = !string.IsNullOrEmpty(reqType) && reqType != "void" ? $"{reqType} request, " : "";

            var pathParams = ExtractPathParams(method.Path);
            var pathParamString = string.Join("", pathParams.Select(p => $"string {ToCamelCase(p)}, "));

            sb.AppendLine($"    {returnType} {method.Name}Async({pathParamString}{paramType}CancellationToken cancellationToken = default);");
        }

        sb.AppendLine("}");

        WriteFile(interfacePath, sb.ToString(), dryRun, projectName);
    }

    private List<string> ExtractPathParams(string path)
    {
        var params_ = new List<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(path, @"\{(\w+)\}");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            params_.Add(match.Groups[1].Value);
        }
        return params_;
    }

    private void GenerateRepository(string projectName, string serviceName, List<RestMethod> methods, string outputPath, bool dryRun)
    {
        var repoPath = Path.Combine(outputPath, "src", $"{projectName}.Infrastructure", "Clients", $"{serviceName}RestClient.cs");

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine($"using {projectName}.Application.Interfaces;");
        sb.AppendLine($"using {projectName}.Application.Models.Clients;");
        sb.AppendLine($"using {projectName}.Common.Http;");
        sb.AppendLine("using System.Net.Http.Json;");
        sb.AppendLine();
        sb.AppendLine($"namespace {projectName}.Infrastructure.Clients;");
        sb.AppendLine();
        sb.AppendLine($"public class {serviceName}RestClient : I{serviceName}RestClient");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly ResilientHttpClient _httpClient;");
        sb.AppendLine($"    private readonly ILogger<{serviceName}RestClient> _logger;");
        sb.AppendLine();
        sb.AppendLine($"    public {serviceName}RestClient(ResilientHttpClient httpClient, ILogger<{serviceName}RestClient> logger)");
        sb.AppendLine("    {");
        sb.AppendLine("        _httpClient = httpClient;");
        sb.AppendLine("        _logger = logger;");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var method in methods)
        {
            var reqType = MapType(method.RequestTypeName ?? "");
            var resType = MapType(method.ResponseTypeName ?? "");

            var returnType = !string.IsNullOrEmpty(resType) && resType != "void" ? $"Task<{resType}?>" : "Task";
            var paramType = !string.IsNullOrEmpty(reqType) && reqType != "void" ? $"{reqType} request, " : "";

            var pathParams = ExtractPathParams(method.Path);
            var pathParamString = string.Join("", pathParams.Select(p => $"string {ToCamelCase(p)}, "));

            sb.AppendLine($"    public async {returnType} {method.Name}Async({pathParamString}{paramType}CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");

            var methodCall = "await Elastic.Apm.Agent.Tracer.CurrentTransaction.CaptureSpan";
            if (returnType.StartsWith("Task<"))
            {
                methodCall = "return " + methodCall;
            }
            sb.AppendLine($"            {methodCall}(");
            sb.AppendLine($"                $\"Call {serviceName}.{method.Name}\",");
            sb.AppendLine("                \"external\",");
            sb.AppendLine("                async () =>");
            sb.AppendLine("            {");

            var interpolatedPath = method.Path;
            foreach (var p in pathParams)
            {
                interpolatedPath = interpolatedPath.Replace("{" + p + "}", "{" + ToCamelCase(p) + "}");
            }

            if (method.HttpMethod.ToUpper() == "GET")
            {
                if (!string.IsNullOrEmpty(resType) && resType != "void")
                {
                    sb.AppendLine($"                return await _httpClient.GetAsync<{resType}>($\"{interpolatedPath}\", cancellationToken);");
                }
                else
                {
                    sb.AppendLine($"                await _httpClient.GetAsync($\"{interpolatedPath}\", cancellationToken);");
                }
            }
            else if (method.HttpMethod.ToUpper() == "POST")
            {
                var reqObj = (method.Request != null && method.Request.Any()) ? "request" : "new { }";
                if (!string.IsNullOrEmpty(resType) && resType != "void")
                {
                    sb.AppendLine($"                return await _httpClient.PostAsync<{resType}>($\"{interpolatedPath}\", {reqObj}, cancellationToken);");
                }
                else
                {
                    sb.AppendLine($"                await _httpClient.PostAsync($\"{interpolatedPath}\", JsonContent.Create({reqObj}), cancellationToken);");
                }
            }
            else if (method.HttpMethod.ToUpper() == "PUT")
            {
                var reqObj = (method.Request != null && method.Request.Any()) ? "request" : "new { }";
                sb.AppendLine($"                await _httpClient.PutAsync($\"{interpolatedPath}\", JsonContent.Create({reqObj}), cancellationToken);");
            }
            else if (method.HttpMethod.ToUpper() == "PATCH")
            {
                var reqObj = (method.Request != null && method.Request.Any()) ? "request" : "new { }";
                sb.AppendLine($"                await _httpClient.PutAsync($\"{interpolatedPath}\", JsonContent.Create({reqObj}), cancellationToken); // Using PutAsync as a substitute if PatchAsync is missing");
            }
            else if (method.HttpMethod.ToUpper() == "DELETE")
            {
                sb.AppendLine($"                await _httpClient.DeleteAsync($\"{interpolatedPath}\", cancellationToken);");
            }
            else
            {
                sb.AppendLine("                await Task.CompletedTask; // Default fallback to avoid CS1998");
            }

            sb.AppendLine("            }, \"http\");");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)");
            sb.AppendLine("        {");
            if (!string.IsNullOrEmpty(resType) && resType != "void")
            {
                sb.AppendLine("            return null;");
            }
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine($"            throw new global::{projectName}.Common.Errors.DataAccessHubException($\"Failed to call {method.Name} via REST\", ex);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        WriteFile(repoPath, sb.ToString(), dryRun, projectName);
    }

    private void UpdateAppOptions(string projectName, string serviceName, string configKey, string outputPath, bool dryRun)
    {
        var appOptionsPath = Path.Combine(outputPath, "src", $"{projectName}.Common", "Configuration", "AppOptions.cs");

        var optionsClassName = $"{serviceName}Options";
        var propertyCode = $@"
    public {optionsClassName} {serviceName} {{ get; set; }} = new();

    public CircuitBreakerOptions GetCircuitBreakerFor{serviceName}() => {serviceName}?.CircuitBreaker ?? CircuitBreaker;
    public TlsOptions GetTlsFor{serviceName}() => {serviceName}?.Tls ?? Tls;";

        var classDefinition = $@"
    public class {optionsClassName}
    {{
        public string BaseUrl {{ get; set; }} = ""http://localhost:5000"";
        public int Timeout {{ get; set; }} = 30;
        public CircuitBreakerOptions? CircuitBreaker {{ get; set; }}
        public TlsOptions? Tls {{ get; set; }}
    }}";

        _codeInjector.InjectAppOption(appOptionsPath, "RestClientOptions", propertyCode, classDefinition);
    }

    private void UpdateAppSettings(string projectName, string serviceName, string configKey, string outputPath, bool dryRun)
    {
        var projectDir = Path.Combine(outputPath, "src", projectName);
        var serviceConfig = $"\n    \"{serviceName}\": {{\n      \"BaseUrl\": \"http://localhost:5000\",\n      \"Timeout\": 30,\n      \"CircuitBreaker\": {{}},\n      \"Tls\": {{ }}\n    }},";
        _codeInjector.InjectAppSettingToAllConfigs(projectDir, "RestClient", serviceConfig);
    }

    private void UpdateDependencyInjection(string projectName, string serviceName, string configKey, string outputPath, bool dryRun)
    {
        var diPath = Path.Combine(outputPath, "src", projectName, "Extensions", "DependencyInjection.cs");

        _codeInjector.InjectUsing(diPath, "Microsoft.Extensions.Options");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Common.Configuration");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Common.Http");
        _codeInjector.InjectUsing(diPath, $"{projectName}.Infrastructure.Clients");

        var diCode = $@"
        // Register {serviceName} REST Client
        services.AddHttpClient<{serviceName}RestClient>((sp, client) =>
        {{
            var config = sp.GetRequiredService<IConfiguration>();
            var options = config.GetSection(""RestClient"").Get<global::{projectName}.Common.Configuration.RestClientOptions>() 
                ?? new global::{projectName}.Common.Configuration.RestClientOptions();
            
            client.BaseAddress = new Uri(options.{serviceName}.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.{serviceName}.Timeout);
        }})
        .AddHttpMessageHandler<global::{projectName}.Common.RestClient.ClaimsPropagationHandler>();

        services.AddScoped<I{serviceName}RestClient, global::{projectName}.Infrastructure.Clients.{serviceName}RestClient>(sp =>
        {{
            var config = sp.GetRequiredService<IConfiguration>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(""{serviceName}RestClient"");
            var logger = sp.GetRequiredService<ILogger<ResilientHttpClient>>();
            var options = config.GetSection(""RestClient"").Get<global::{projectName}.Common.Configuration.RestClientOptions>() 
                ?? new global::{projectName}.Common.Configuration.RestClientOptions();

            var cbOptions = options.GetCircuitBreakerFor{serviceName}();

            var clientOptions = new global::{projectName}.Common.Http.HttpClientOptions
            {{
                MaxRetries = options.Retry.MaxAttempts,
                RetryDelayMs = options.Retry.DelayMs,
                CircuitBreakerFailureThreshold = cbOptions.FailureRatio,
                CircuitBreakerMinThroughput = cbOptions.MinRequests,
                CircuitBreakerDurationSeconds = cbOptions.IntervalSeconds
            }};

            var resilientClient = new ResilientHttpClient(httpClient, logger, clientOptions);
            return new global::{projectName}.Infrastructure.Clients.{serviceName}RestClient(resilientClient, sp.GetRequiredService<ILogger<global::{projectName}.Infrastructure.Clients.{serviceName}RestClient>>());
        }});";

        _codeInjector.InjectDependency(diPath, diCode, "AddInfrastructure");
    }

    private string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var parts = input.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower()));
    }

    private string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var pascal = ToPascalCase(input);
        return char.ToLower(pascal[0]) + pascal.Substring(1);
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

public class RestClientDefinition
{
    [JsonPropertyName("service_name")]
    public string ServiceName { get; set; } = "";

    [JsonPropertyName("base_url_config_key")]
    public string BaseUrlConfigKey { get; set; } = "";

    [JsonPropertyName("methods")]
    public List<RestMethod> Methods { get; set; } = new();
}

public class RestMethod
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("http_method")]
    public string HttpMethod { get; set; } = "GET";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("request")]
    public Dictionary<string, string>? Request { get; set; }

    [JsonPropertyName("response")]
    public Dictionary<string, string>? Response { get; set; }

    [JsonPropertyName("request_type_name")]
    public string? RequestTypeName { get; set; }

    [JsonPropertyName("response_type_name")]
    public string? ResponseTypeName { get; set; }
}
