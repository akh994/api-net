using System.Text;
using System.Text.RegularExpressions;
using SkeletonApi.Generator.Models;
using Spectre.Console;

namespace SkeletonApi.Generator.Services;

public class GrpcClientGenerator
{
    private readonly ProtoParser _protoParser;
    private readonly CodeInjector _codeInjector;

    public GrpcClientGenerator(CodeInjector codeInjector)
    {
        _protoParser = new ProtoParser();
        _codeInjector = codeInjector;
    }

    public void Generate(string protoPath, string serviceName, string outputPath, bool dryRun)
    {
        if (!File.Exists(protoPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Proto file not found: {protoPath}[/]");
            return;
        }

        // Determine project name from output path early
        var projectName = _codeInjector.DetectProjectName(outputPath);
        if (string.IsNullOrEmpty(projectName))
        {
            AnsiConsole.MarkupLine("[red]Error: Could not detect project name from output path[/]");
            return;
        }

        // Auto-detect if project uses remote common pkg
        _codeInjector.AutoDetectRemoteConfig(outputPath);

        AnsiConsole.MarkupLine($"[cyan]Detected project: {projectName}[/]");

        // Ensure Contracts project exists
        EnsureContractsProject(projectName, outputPath, dryRun);
        EnsureContractsProjectReference(projectName, outputPath, dryRun);

        var protoContent = File.ReadAllText(protoPath);
        var protoDirectory = Path.GetDirectoryName(Path.GetFullPath(protoPath)) ?? string.Empty;

        // Handle Imports and get list of imported files
        var importedFiles = HandleImports(protoContent, protoDirectory, outputPath, projectName, dryRun);

        // Extract service information using brace counting
        var serviceBody = ExtractServiceBody(protoContent, out string actualServiceName);

        if (string.IsNullOrEmpty(serviceBody))
        {
            AnsiConsole.MarkupLine("[red]Error: No service definition found in proto file[/]");
            return;
        }

        // Extract RPC methods
        var rpcMatches = Regex.Matches(serviceBody, @"rpc\s+(\w+)\s*\((\w+)\)\s*returns\s*\((\w+)\)");
        var methods = new List<(string Name, string Request, string Response)>();

        foreach (Match rpcMatch in rpcMatches)
        {
            methods.Add((rpcMatch.Groups[1].Value, rpcMatch.Groups[2].Value, rpcMatch.Groups[3].Value));
        }

        if (!methods.Any())
        {
            AnsiConsole.MarkupLine("[yellow]Warning: No RPC methods found in service[/]");
        }

        AnsiConsole.MarkupLine($"[cyan]Service: {actualServiceName}[/]");
        AnsiConsole.MarkupLine($"[cyan]Methods: {methods.Count}[/]");

        // Generate files
        GenerateInterface(projectName, actualServiceName, methods, outputPath, dryRun);
        GenerateRepository(projectName, actualServiceName, methods, outputPath, dryRun);
        GenerateProtoFile(protoContent, projectName, actualServiceName, outputPath, dryRun);
        UpdateProjectFile(projectName, actualServiceName, outputPath, importedFiles, dryRun);
        UpdateAppOptions(projectName, actualServiceName, outputPath, dryRun);
        UpdateAppSettings(projectName, actualServiceName, outputPath, dryRun);
        UpdateDependencyInjection(projectName, actualServiceName, outputPath, dryRun);

        AnsiConsole.MarkupLine($"[green]✓ gRPC client generation completed for {actualServiceName}[/]");
    }

    private void EnsureContractsProject(string projectName, string outputPath, bool dryRun)
    {
        var contractsDir = Path.Combine(outputPath, "src", $"{projectName}.Contracts");
        var csprojPath = Path.Combine(contractsDir, $"{projectName}.Contracts.csproj");

        if (File.Exists(csprojPath))
        {
            return; // Already exists
        }

        // Create Contracts.csproj based on skeleton template
        var csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <SuppressTfmSupportBuildWarnings>true</SuppressTfmSupportBuildWarnings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Grpc.Tools"" Version=""2.71.0"">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include=""Google.Api.CommonProtos"" Version=""2.10.0"" />
    <PackageReference Include=""Google.Protobuf"" Version=""3.30.2"" />
    <PackageReference Include=""Grpc.Net.Client"" Version=""2.71.0"" />
    <PackageReference Include=""Grpc.AspNetCore"" Version=""2.71.0"" />
  </ItemGroup>

</Project>
";

        WriteFile(csprojPath, csprojContent, dryRun, projectName);

        if (!dryRun)
        {
            AnsiConsole.MarkupLine($"[green]✓ Created {projectName}.Contracts project[/]");
        }
    }

    private void EnsureContractsProjectReference(string projectName, string outputPath, bool dryRun)
    {
        var applicationCsprojPath = Path.Combine(outputPath, "src", $"{projectName}.Application", $"{projectName}.Application.csproj");

        if (!File.Exists(applicationCsprojPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: {projectName}.Application.csproj not found[/]");
            return;
        }

        var content = File.ReadAllText(applicationCsprojPath);
        var projectRef = $@"<ProjectReference Include=""..\{projectName}.Contracts\{projectName}.Contracts.csproj"" />";

        if (content.Contains(projectRef))
        {
            return; // Already referenced
        }

        // Find existing ProjectReference ItemGroup or create new one
        var projectRefMatch = Regex.Match(content, @"<ProjectReference\s+Include");
        if (projectRefMatch.Success)
        {
            content = content.Insert(projectRefMatch.Index, projectRef + "\n    ");
        }
        else
        {
            var projectEndMatch = Regex.Match(content, @"</Project>");
            if (projectEndMatch.Success)
            {
                content = content.Insert(projectEndMatch.Index, $"  <ItemGroup>\n    {projectRef}\n  </ItemGroup>\n");
            }
        }

        WriteFile(applicationCsprojPath, content, dryRun, projectName);
    }

    private string ExtractServiceBody(string content, out string serviceName)
    {
        serviceName = string.Empty;
        var serviceMatch = Regex.Match(content, @"service\s+(\w+)\s*\{");

        if (!serviceMatch.Success) return string.Empty;

        serviceName = serviceMatch.Groups[1].Value;
        int startIndex = serviceMatch.Index + serviceMatch.Length - 1; // start at {

        int braceCount = 0;
        int endIndex = -1;

        for (int i = startIndex; i < content.Length; i++)
        {
            if (content[i] == '{') braceCount++;
            else if (content[i] == '}') braceCount--;

            if (braceCount == 0)
            {
                endIndex = i;
                break;
            }
        }

        if (endIndex != -1)
        {
            return content.Substring(startIndex + 1, endIndex - startIndex - 1);
        }

        return string.Empty;
    }

    private List<string> HandleImports(string protoContent, string sourceDir, string outputPath, string projectName, bool dryRun)
    {
        var importedFiles = new List<string>();
        var importMatches = Regex.Matches(protoContent, @"import\s+""([^""]+)""\s*;");
        var outputProtosDir = Path.Combine(outputPath, "src", _codeInjector.DetectProjectName(outputPath) + ".Contracts", "Protos");

        foreach (Match match in importMatches)
        {
            var importPath = match.Groups[1].Value;
            var fileName = Path.GetFileName(importPath);

            // Try 1: Exact path relative to sourceDir
            var sourcePath1 = Path.Combine(sourceDir, importPath);
            // Try 2: Flat in sourceDir
            var sourcePath2 = Path.Combine(sourceDir, fileName);

            string? foundPath = null;
            if (File.Exists(sourcePath1)) foundPath = sourcePath1;
            else if (File.Exists(sourcePath2)) foundPath = sourcePath2;

            if (foundPath != null)
            {
                var content = File.ReadAllText(foundPath);

                // Rewrite csharp_namespace in imported file to match project structure
                // Assuming imported files (like Common.proto) should share the project's Protos namespace
                var targetNamespace = $"{projectName}.Protos";
                var newOption = $"option csharp_namespace = \"{targetNamespace}\";";

                if (content.Contains("option csharp_namespace"))
                {
                    content = Regex.Replace(content, @"option\s+csharp_namespace\s*=\s*""[^""]+""\s*;", newOption);
                }
                else
                {
                    var syntaxMatch = Regex.Match(content, @"syntax\s*=\s*""proto3""\s*;");
                    if (syntaxMatch.Success)
                    {
                        content = content.Insert(syntaxMatch.Index + syntaxMatch.Length, "\n\n" + newOption);
                    }
                    else
                    {
                        content = newOption + "\n\n" + content;
                    }
                }

                WriteFile(Path.Combine(outputProtosDir, fileName), content, dryRun, projectName);
                importedFiles.Add(fileName);
            }
            else
            {
                // Warn but don't fail, it might be a standard google proto
                if (!importPath.StartsWith("google/"))
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Could not find imported file: {importPath}[/]");
                }
            }
        }
        return importedFiles;
    }

    private void GenerateInterface(string projectName, string serviceName, List<(string Name, string Request, string Response)> methods, string outputPath, bool dryRun)
    {
        var interfacePath = Path.Combine(outputPath, "src", $"{projectName}.Application", "Interfaces", $"I{serviceName}GrpcRepository.cs");

        var sb = new StringBuilder();
        sb.AppendLine($"namespace {projectName}.Application.Interfaces;");
        sb.AppendLine();
        sb.AppendLine($"public interface I{serviceName}GrpcRepository");
        sb.AppendLine("{");

        foreach (var method in methods)
        {
            sb.AppendLine($"    Task<global::{projectName}.Protos.Clients.{serviceName}.{method.Response}> {method.Name}Async(global::{projectName}.Protos.Clients.{serviceName}.{method.Request} request, CancellationToken cancellationToken = default);");
        }

        sb.AppendLine("}");

        WriteFile(interfacePath, sb.ToString(), dryRun, projectName);
    }

    private void GenerateRepository(string projectName, string serviceName, List<(string Name, string Request, string Response)> methods, string outputPath, bool dryRun)
    {
        var repoPath = Path.Combine(outputPath, "src", $"{projectName}.Infrastructure", "Clients", $"{serviceName}GrpcRepository.cs");

        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine($"using {projectName}.Application.Interfaces;");
        sb.AppendLine($"using {projectName}.Common.GrpcClient;");
        sb.AppendLine();
        sb.AppendLine($"namespace {projectName}.Infrastructure.Clients;");
        sb.AppendLine();
        sb.AppendLine($"public class {serviceName}GrpcRepository : I{serviceName}GrpcRepository");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly GrpcClientRepository<global::{projectName}.Protos.Clients.{serviceName}.{serviceName}.{serviceName}Client> _repository;");
        sb.AppendLine($"    private readonly ILogger<{serviceName}GrpcRepository> _logger;");
        sb.AppendLine();
        sb.AppendLine($"    public {serviceName}GrpcRepository(GrpcClientRepository<global::{projectName}.Protos.Clients.{serviceName}.{serviceName}.{serviceName}Client> repository, ILogger<{serviceName}GrpcRepository> logger)");
        sb.AppendLine("    {");
        sb.AppendLine("        _repository = repository;");
        sb.AppendLine("        _logger = logger;");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var method in methods)
        {
            sb.AppendLine($"    public async Task<global::{projectName}.Protos.Clients.{serviceName}.{method.Response}> {method.Name}Async(global::{projectName}.Protos.Clients.{serviceName}.{method.Request} request, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            return await _repository.CallAsync(async client => await client.{method.Name}Async(request, cancellationToken: cancellationToken));");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _logger.LogError(ex, \"Error calling gRPC method {method.Name}\");");
            sb.AppendLine("            throw;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        WriteFile(repoPath, sb.ToString(), dryRun, projectName);
    }

    private void GenerateProtoFile(string protoContent, string projectName, string serviceName, string outputPath, bool dryRun)
    {
        var protoPath = Path.Combine(outputPath, "src", $"{projectName}.Contracts", "Protos", $"{serviceName.ToLower()}.proto");

        // Force project-specific csharp_namespace
        var targetNamespace = $"{projectName}.Protos.Clients.{serviceName}";
        var newOption = $"option csharp_namespace = \"{targetNamespace}\";";

        var updatedProtoContent = protoContent;
        if (updatedProtoContent.Contains("option csharp_namespace"))
        {
            updatedProtoContent = Regex.Replace(updatedProtoContent, @"option\s+csharp_namespace\s*=\s*""[^""]+""\s*;", newOption);
        }
        else
        {
            // Insert after syntax line
            var syntaxMatch = Regex.Match(updatedProtoContent, @"syntax\s*=\s*""proto3""\s*;");
            if (syntaxMatch.Success)
            {
                updatedProtoContent = updatedProtoContent.Insert(syntaxMatch.Index + syntaxMatch.Length, "\n\n" + newOption);
            }
            else
            {
                updatedProtoContent = newOption + "\n\n" + updatedProtoContent;
            }
        }

        // Rewrite imports to be flat
        updatedProtoContent = Regex.Replace(updatedProtoContent, @"import\s+""([^""]+)""\s*;", match =>
        {
            var path = match.Groups[1].Value;
            if (path == "google/api/annotations.proto") return ""; // Remove annotations import
            if (path.StartsWith("google/")) return match.Value; // Keep other google imports as is
            return $"import \"{Path.GetFileName(path)}\";";
        });

        // Remove google.api.http options
        updatedProtoContent = Regex.Replace(updatedProtoContent, @"option\s*\(google\.api\.http\)\s*=\s*\{[\s\S]*?\};", "");

        WriteFile(protoPath, updatedProtoContent, dryRun, projectName);
    }

    private void UpdateProjectFile(string projectName, string serviceName, string outputPath, List<string> importedFiles, bool dryRun)
    {
        var csprojPath = Path.Combine(outputPath, "src", $"{projectName}.Contracts", $"{projectName}.Contracts.csproj");
        if (!File.Exists(csprojPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: {projectName}.Contracts.csproj not found, skipping proto references update[/]");
            return;
        }

        var content = File.ReadAllText(csprojPath);

        // 1. Add Service Proto
        var protoRef = $@"<Protobuf Include=""Protos\{serviceName.ToLower()}.proto"" GrpcServices=""Client"" ProtoRoot=""Protos"" />";
        if (!content.Contains(protoRef))
        {
            // Try to find an ItemGroup that already has GrpcClient protobufs
            var protobufMatch = Regex.Match(content, @"<Protobuf\s+Include=""[^""]+""\s+GrpcServices=""Client""\s*/>");
            if (protobufMatch.Success)
            {
                content = content.Insert(protobufMatch.Index, protoRef + "\n    ");
            }
            else
            {
                // Fallback: Add a new ItemGroup before the end of the Project
                var projectEndMatch = Regex.Match(content, @"</Project>");
                if (projectEndMatch.Success)
                {
                    content = content.Insert(projectEndMatch.Index, $"  <ItemGroup>\n    {protoRef}\n  </ItemGroup>\n");
                }
            }
        }

        // 2. Add Imported Protos (e.g. Common.proto), without GrpcServices attribute (messages only)
        foreach (var file in importedFiles)
        {
            var importedRef = $@"<Protobuf Include=""Protos\{file}"" ProtoRoot=""Protos"" />";
            if (!content.Contains(importedRef))
            {
                // Try to find an ItemGroup that already has Protobufs (but not necessarily Client)
                var protobufMatch = Regex.Match(content, @"<Protobuf\s+Include=""[^""]+""");
                if (protobufMatch.Success)
                {
                    content = content.Insert(protobufMatch.Index, importedRef + "\n    ");
                }
                else
                {
                    var projectEndMatch = Regex.Match(content, @"</Project>");
                    if (projectEndMatch.Success)
                    {
                        content = content.Insert(projectEndMatch.Index, $"  <ItemGroup>\n    {importedRef}\n  </ItemGroup>\n");
                    }
                }
            }
        }

        WriteFile(csprojPath, content, dryRun, projectName);
    }

    private void UpdateAppOptions(string projectName, string serviceName, string outputPath, bool dryRun)
    {
        var appOptionsPath = Path.Combine(outputPath, "src", $"{projectName}.Common", "Configuration", "AppOptions.cs");

        if (!File.Exists(appOptionsPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: AppOptions.cs not found: {appOptionsPath}[/]");
            return;
        }

        // Remove "Service" suffix if present to avoid duplication
        var baseName = serviceName.EndsWith("Service") ? serviceName.Substring(0, serviceName.Length - 7) : serviceName;
        var servicePropertyName = $"{baseName}Service";

        var propertyCode = $@"
    public GrpcServiceOptions {servicePropertyName} {{ get; set; }} = new();

    public CircuitBreakerOptions GetCircuitBreakerFor{servicePropertyName}() => {servicePropertyName}?.CircuitBreaker ?? CircuitBreaker;
    public TlsOptions GetTlsFor{servicePropertyName}() => {servicePropertyName}?.Tls ?? Tls;";

        _codeInjector.InjectAppOption(appOptionsPath, "GrpcClientOptions", propertyCode);
    }

    private void UpdateAppSettings(string projectName, string serviceName, string outputPath, bool dryRun)
    {
        var projectDir = Path.Combine(outputPath, "src", projectName);

        // Remove "Service" suffix if present to avoid duplication
        var baseName = serviceName.EndsWith("Service") ? serviceName.Substring(0, serviceName.Length - 7) : serviceName;
        var servicePropertyName = $"{baseName}Service";

        var section = $"\n    \"{servicePropertyName}\": {{\n      \"Host\": \"localhost\",\n      \"Port\": 50051,\n      \"Address\": \"http://localhost:50051\",\n      \"CircuitBreaker\": {{}},\n      \"Tls\": {{ }}\n    }},";
        var baseSettingsPath = Path.Combine(projectDir, "appsettings.json");
        _codeInjector.InjectAppSetting(baseSettingsPath, "GrpcClient", section);
    }

    private void UpdateDependencyInjection(string projectName, string serviceName, string outputPath, bool dryRun)
    {
        var diPath = Path.Combine(outputPath, "src", projectName, "Extensions", "DependencyInjection.cs");

        if (!File.Exists(diPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: DependencyInjection.cs not found: {diPath}[/]");
            return;
        }

        var content = File.ReadAllText(diPath);

        // Remove "Service" suffix if present to avoid duplication
        var baseName = serviceName.EndsWith("Service") ? serviceName.Substring(0, serviceName.Length - 7) : serviceName;
        var servicePropertyName = $"{baseName}Service";

        // Generate DI registration code
        var diCode = $@"
        // Register {serviceName} gRPC Client
        services.AddGrpcClient<global::{projectName}.Protos.Clients.{serviceName}.{serviceName}.{serviceName}Client>(o =>
        {{
            var grpcOptions = configuration.GetSection(""GrpcClient"").Get<global::{projectName}.Common.Configuration.GrpcClientOptions>() 
                ?? new global::{projectName}.Common.Configuration.GrpcClientOptions();
            o.Address = new Uri(grpcOptions.{servicePropertyName}?.Address ?? ""http://localhost:50051"");
        }});

        // Register GrpcClientRepository for {serviceName}
        services.AddScoped<global::{projectName}.Common.GrpcClient.GrpcClientRepository<global::{projectName}.Protos.Clients.{serviceName}.{serviceName}.{serviceName}Client>>(sp =>
        {{
            var logger = sp.GetRequiredService<ILogger<global::{projectName}.Common.GrpcClient.GrpcClientRepository<global::{projectName}.Protos.Clients.{serviceName}.{serviceName}.{serviceName}Client>>>();
            var interceptor = sp.GetRequiredService<global::{projectName}.Common.GrpcClient.ClaimsPropagationInterceptor>();
            var configOptions = configuration.GetSection(""GrpcClient"").Get<global::{projectName}.Common.Configuration.GrpcClientOptions>() 
                ?? new global::{projectName}.Common.Configuration.GrpcClientOptions();
            
            var address = configOptions.{servicePropertyName}?.Address ?? ""http://localhost:50051"";
            
            return new global::{projectName}.Common.GrpcClient.GrpcClientRepository<global::{projectName}.Protos.Clients.{serviceName}.{serviceName}.{serviceName}Client>(
                address,
                configOptions.GetCircuitBreakerFor{servicePropertyName}(),
                configOptions.GetTlsFor{servicePropertyName}(),
                logger,
                channel => new global::{projectName}.Protos.Clients.{serviceName}.{serviceName}.{serviceName}Client(channel),
                interceptor
            );
        }});
        // Register {serviceName}GrpcRepository
        services.AddScoped<I{serviceName}GrpcRepository, global::{projectName}.Infrastructure.Clients.{serviceName}GrpcRepository>();
";

        _codeInjector.InjectDependency(diPath, diCode, "AddInfrastructure");
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
