using System.Text.RegularExpressions;
using Spectre.Console;

namespace SkeletonApi.Generator.Services;

public class NewConsumerProjectGenerator
{
    private readonly string _skeletonPath;
    private readonly CodeInjector _codeInjector;

    public NewConsumerProjectGenerator(string skeletonPath, CodeInjector codeInjector)
    {
        _skeletonPath = skeletonPath;
        _codeInjector = codeInjector;
    }

    public async Task GenerateAsync(
        string inputPath,
        string projectName,
        string outputDir,
        bool dryRun,
        bool update = false)
    {
        var targetDir = string.IsNullOrEmpty(outputDir) ? projectName : outputDir;
        targetDir = Path.GetFullPath(targetDir);

        if (Directory.Exists(targetDir) && !dryRun)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: Target directory {targetDir} already exists.[/]");
        }

        AnsiConsole.MarkupLine($"[blue]Generating consumer project: {projectName}[/]");

        // 1. Create Solution Structure
        if (!dryRun)
        {
            Directory.CreateDirectory(targetDir);
            Directory.CreateDirectory(Path.Combine(targetDir, "src"));
        }

        if (!update)
        {
            // 2. Copy/Generate Common, Domain, Application, Infrastructure
            // We can reuse the structure but we need to be careful about what we copy.
            // For a simple consumer, we might not need everything, but sticking to the Clean Architecture 
            // structure of the skeleton is good for consistency.

            // Copy Common
            await CopyDirectoryWithRenameAsync(
                Path.Combine(_skeletonPath, "src", "SkeletonApi.Common"),
                Path.Combine(targetDir, "src", $"{projectName}.Common"),
                projectName, dryRun);

            // Copy Domain
            await GenerateDomainProjectAsync(projectName, targetDir, dryRun);

            // Copy Application (Empty initially)
            await GenerateApplicationProjectAsync(projectName, targetDir, dryRun);

            // Copy Infrastructure (Empty initially)
            await GenerateInfrastructureProjectAsync(projectName, targetDir, dryRun);

            // 3. Generate Worker Service Project
            await GenerateWorkerProjectAsync(projectName, targetDir, dryRun);

            // 4. Copy Support Files
            await CopySupportFilesAsync(targetDir, projectName, dryRun);

            // 5. Create Solution File
            if (!dryRun)
            {
                await CreateSolutionAsync(targetDir, projectName);
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]Update mode: Skipping base project structure generation[/]");
        }

        // 6. Run MqConsumerGenerator to populate consumers
        AnsiConsole.MarkupLine($"[blue]Generating consumers from {inputPath}...[/]");
        var consumerGenerator = new MqConsumerGenerator(_codeInjector);
        // The MqConsumerGenerator expects the project structure to exist.
        // It looks for src/{ProjectName} for the main entry point (Consumers folder).
        // Our Worker Service is named {ProjectName}, so it matches src/{ProjectName}.
        consumerGenerator.Generate(inputPath, targetDir, dryRun);

        if (!dryRun)
        {
            // 6. Format generated code
            AnsiConsole.MarkupLine("[blue]Formatting generated code...[/]");
            await RunCommandAsync("dotnet", "format --no-restore", targetDir);

            AnsiConsole.MarkupLine($"\n[green]Consumer project generated successfully![/]");
        }
    }

    private async Task GenerateDomainProjectAsync(string projectName, string targetDir, bool dryRun)
    {
        var domainDir = Path.Combine(targetDir, "src", $"{projectName}.Domain");
        await CopyFileWithRenameAsync(
            Path.Combine(_skeletonPath, "src", "SkeletonApi.Domain", "SkeletonApi.Domain.csproj"),
            Path.Combine(domainDir, $"{projectName}.Domain.csproj"),
            projectName, dryRun);

        // Create Entities folder
        if (!dryRun) Directory.CreateDirectory(Path.Combine(domainDir, "Entities"));
    }

    private async Task GenerateApplicationProjectAsync(string projectName, string targetDir, bool dryRun)
    {
        var appDir = Path.Combine(targetDir, "src", $"{projectName}.Application");
        await CopyFileWithRenameAsync(
            Path.Combine(_skeletonPath, "src", "SkeletonApi.Application", "SkeletonApi.Application.csproj"),
            Path.Combine(appDir, $"{projectName}.Application.csproj"),
            projectName, dryRun);

        // Create folders
        if (!dryRun)
        {
            Directory.CreateDirectory(Path.Combine(appDir, "Interfaces"));
            Directory.CreateDirectory(Path.Combine(appDir, "Services"));
            Directory.CreateDirectory(Path.Combine(appDir, "Models"));
            Directory.CreateDirectory(Path.Combine(appDir, "Validators"));
        }
    }

    private async Task GenerateInfrastructureProjectAsync(string projectName, string targetDir, bool dryRun)
    {
        var infraDir = Path.Combine(targetDir, "src", $"{projectName}.Infrastructure");
        await CopyFileWithRenameAsync(
            Path.Combine(_skeletonPath, "src", "SkeletonApi.Infrastructure", "SkeletonApi.Infrastructure.csproj"),
            Path.Combine(infraDir, $"{projectName}.Infrastructure.csproj"),
            projectName, dryRun);

        // Create folders
        if (!dryRun)
        {
            Directory.CreateDirectory(Path.Combine(infraDir, "Messaging"));
            Directory.CreateDirectory(Path.Combine(infraDir, "Repositories"));
        }
    }

    private async Task GenerateWorkerProjectAsync(string projectName, string targetDir, bool dryRun)
    {
        var workerDir = Path.Combine(targetDir, "src", projectName);
        if (!dryRun) Directory.CreateDirectory(workerDir);

        // 1. Project File
        // We can base it on SkeletonApi.csproj but change Sdk to Microsoft.NET.Sdk.Worker if we want, 
        // or keep Microsoft.NET.Sdk.Web if we want to expose health checks via HTTP.
        // Let's use Microsoft.NET.Sdk.Worker for a pure worker, but often consumers need health checks.
        // The skeleton uses Microsoft.NET.Sdk.Web. Let's stick to that for consistency and ease of health check integration.
        // Actually, let's look at SkeletonApi.csproj.

        var csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk.Web"">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>dotnet-{projectName}-{Guid.NewGuid()}</UserSecretsId>
    <DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.VisualStudio.Azure.Containers.Tools.Targets"" Version=""1.18.1"" />
    <PackageReference Include=""Elastic.Apm.NetCoreAll"" Version=""1.34.1"" />
    <PackageReference Include=""Serilog.AspNetCore"" Version=""10.0.0"" />
    <PackageReference Include=""Serilog.Enrichers.Environment"" Version=""2.3.0"" />
    <PackageReference Include=""Serilog.Sinks.Elasticsearch"" Version=""10.0.0"" />
    <PackageReference Include=""Swashbuckle.AspNetCore"" Version=""7.2.0"" />
    <PackageReference Include=""FluentValidation.DependencyInjectionExtensions"" Version=""11.9.2"" />
    <PackageReference Include=""Microsoft.Extensions.DependencyInjection"" Version=""10.0.3"" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include=""..\{projectName}.Application\{projectName}.Application.csproj"" />
    <ProjectReference Include=""..\{projectName}.Infrastructure\{projectName}.Infrastructure.csproj"" />
    <ProjectReference Include=""..\{projectName}.Common\{projectName}.Common.csproj"" />
  </ItemGroup>

</Project>
";
        await WriteFileAsync(Path.Combine(workerDir, $"{projectName}.csproj"), csprojContent, dryRun);

        // 2. Program.cs
        var programContent = $@"using {projectName}.Common.Configuration;
using {projectName}.Extensions;
using {projectName}.Endpoints;
using {projectName}.Common.Middleware;
using {projectName}.Common.Extensions;
using Serilog;
using Elastic.Apm.NetCoreAll;
using Elastic.Apm.SerilogEnricher;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use ports from appsettings
builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{{
    var serverConfig = context.Configuration.GetSection(""Server"").Get<ServerOptions>() ?? new ServerOptions();
    serverOptions.ListenAnyIP(serverConfig.HttpPort);
}});

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithElasticApmCorrelationInfo()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{{
    c.SwaggerDoc(""v1"", new Microsoft.OpenApi.Models.OpenApiInfo {{ Title = ""{projectName} API"", Version = ""v1"" }});
}});
builder.Services.AddHealthChecks();
builder.Services.AddAllElasticApm();

// Add Layer Dependencies
builder.Services.AddConfigurationOptions(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

// Register feature flag services
builder.Services.AddFeatureFlagServices(builder.Configuration);
builder.Services.AddCommonCors(builder.Configuration);

// Application layer services will be registered by MqConsumerGenerator or manually

// Configure graceful shutdown timeout
builder.Services.Configure<HostOptions>(options =>
{{
    var serverConfig = builder.Configuration.GetSection(""Server"").Get<ServerOptions>() ?? new ServerOptions();
    options.ShutdownTimeout = TimeSpan.FromSeconds(serverConfig.ShutdownTimeoutSeconds > 0 ? serverConfig.ShutdownTimeoutSeconds : 30);
}});

var app = builder.Build();

// Add Elastic APM - captures all HTTP requests, database calls, and errors
// app.UseAllElasticApm(builder.Configuration); // Replaced by AddAllElasticApm in services

// Initialize diagnostic server if enabled
{projectName}.Common.Diagnostics.DiagnosticServer? diagnosticServer = null;
var profilingOptions = builder.Configuration.GetSection(""Profiling"").Get<ProfilingOptions>();
if (profilingOptions?.Enabled == true)
{{
    var diagnosticLogger = app.Services.GetRequiredService<ILogger<{projectName}.Common.Diagnostics.DiagnosticServer>>();
    diagnosticServer = new {projectName}.Common.Diagnostics.DiagnosticServer(
        profilingOptions.Host,
        profilingOptions.Port,
        diagnosticLogger
    );
    
    _ = diagnosticServer.StartAsync();
    
    Log.Information(""Diagnostic profiling enabled at {{Address}}"", 
        $""http://{{profilingOptions.Host}}:{{profilingOptions.Port}}/debug/diagnostics/"");
}}
else
{{
    Log.Information(""Diagnostic profiling disabled"");
}}

// Configure the HTTP request pipeline.
var serverConfig = builder.Configuration.GetSection(""Server"").Get<ServerOptions>() ?? new ServerOptions();

if (app.Environment.IsDevelopment())
{{
    app.UseWhen(context => context.Connection.LocalPort == serverConfig.HttpPort, builder =>
    {{
        builder.UseSwagger();
        builder.UseSwaggerUI();
    }});
}}

// Serilog request logging - captures HTTP request details
app.UseSerilogRequestLogging(options =>
{{
    options.GetLevel = (httpContext, elapsed, ex) =>
    {{
        var path = httpContext.Request.Path.Value;
        if (!string.IsNullOrEmpty(path) && serverConfig.ExcludedLogPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {{
            return Serilog.Events.LogEventLevel.Verbose;
        }}
        return ex != null || httpContext.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error : Serilog.Events.LogEventLevel.Information;
    }};
}});
app.UseCors(""AllowAll"");

app.UseAuthentication();

// Extract claims from headers
app.UseClaimsMiddleware();

// Wrap REST API responses with standardized format
app.UseApiResponseWrapper();

app.MapControllers();
app.MapHealthChecks(""/health"");

// Map Feature Flags endpoint
var serverOptions = builder.Configuration.GetSection(""Server"").Get<ServerOptions>() ?? new ServerOptions();
app.MapFeatureFlagEndpoint(serverOptions.HttpPort);

// Register shutdown handler for diagnostic server
if (diagnosticServer != null)
{{
    var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    appLifetime.ApplicationStopping.Register(() =>
    {{
        diagnosticServer.StopAsync().GetAwaiter().GetResult();
    }});
}}

app.Run();
";
        await WriteFileAsync(Path.Combine(workerDir, "Program.cs"), programContent, dryRun);

        // 2.5 Properties/launchSettings.json
        var propertiesDir = Path.Combine(workerDir, "Properties");
        if (!dryRun) Directory.CreateDirectory(propertiesDir);
        var launchSettingsContent = $@"{{
  ""profiles"": {{
    ""http"": {{
      ""commandName"": ""Project"",
      ""dotnetRunMessages"": true,
      ""launchBrowser"": true,
      ""launchUrl"": ""swagger"",
      ""applicationUrl"": ""http://localhost:4021"",
      ""environmentVariables"": {{
        ""ASPNETCORE_ENVIRONMENT"": ""Development""
      }}
    }}
  }}
}}";
        await WriteFileAsync(Path.Combine(propertiesDir, "launchSettings.json"), launchSettingsContent, dryRun);

        // 3. DependencyInjection.cs (Extensions)
        var diDir = Path.Combine(workerDir, "Extensions");
        if (!dryRun) Directory.CreateDirectory(diDir);

        var diContent = $@"using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using {projectName}.Common.Extensions;

namespace {projectName}.Extensions;

public static class DependencyInjection
{{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {{
        // Register Redis Master Connection
        services.AddKeyedSingleton<StackExchange.Redis.IConnectionMultiplexer>(""RedisMaster"", (sp, key) =>
        {{
            var config = sp.GetRequiredService<IConfiguration>();
            var options = config.GetSection(""Cache"").Get<{projectName}.Common.Configuration.CacheOptions>()
                ?? new {projectName}.Common.Configuration.CacheOptions();

            var configOptions = StackExchange.Redis.ConfigurationOptions.Parse(options.GetConnectionString());
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectRetry = 3;
            configOptions.ConnectTimeout = 5000;
            configOptions.SyncTimeout = 5000;

            return StackExchange.Redis.ConnectionMultiplexer.Connect(configOptions);
        }});

        // Register Redis Replica Connection (if configured)
        services.AddKeyedSingleton<StackExchange.Redis.IConnectionMultiplexer>(""RedisReplica"", (sp, key) =>
        {{
            var config = sp.GetRequiredService<IConfiguration>();
            var options = config.GetSection(""Cache"").Get<{projectName}.Common.Configuration.CacheOptions>()
                ?? new {projectName}.Common.Configuration.CacheOptions();

            // If replica is configured, use it; otherwise fallback to master
            if (options.Replica != null)
            {{
                var configOptions = StackExchange.Redis.ConfigurationOptions.Parse(options.Replica.GetConnectionString());
                configOptions.AbortOnConnectFail = false;
                configOptions.ConnectRetry = 3;
                configOptions.ConnectTimeout = 5000;
                configOptions.SyncTimeout = 5000;

                return StackExchange.Redis.ConnectionMultiplexer.Connect(configOptions);
            }}
            else
            {{
                // Fallback to master if no replica configured
                return sp.GetRequiredKeyedService<StackExchange.Redis.IConnectionMultiplexer>(""RedisMaster"");
            }}
        }});

        // Register default IConnectionMultiplexer as master for backward compatibility
        services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
            sp.GetRequiredKeyedService<StackExchange.Redis.IConnectionMultiplexer>(""RedisMaster""));

        // Register Messaging Services
        services.AddSingleton<{projectName}.Common.Messaging.MessagingConnectionManager>();
        services.AddSingleton<{projectName}.Common.Messaging.MessageClientFactory>();
        services.AddSingleton<{projectName}.Common.Messaging.MessageClientProvider>();

        // Infrastructure services registration
        return services;
    }}

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {{
        // Register Common Services
        services.AddCommonServices();
        
        // Application services registration
        return services;
    }}
}}
";
        await WriteFileAsync(Path.Combine(diDir, "DependencyInjection.cs"), diContent, dryRun);

        // 4. ConfigurationExtensions.cs
        var configExtContent = $@"using Microsoft.Extensions.DependencyInjection;
using {projectName}.Common.Configuration;

namespace {projectName}.Extensions;

public static class ConfigurationExtensions
{{
    public static IServiceCollection AddConfigurationOptions(this IServiceCollection services, IConfiguration configuration)
    {{
        // Bind all configuration sections
        services.Configure<ServerOptions>(configuration.GetSection(""Server""));
        services.Configure<DatabaseOptions>(configuration.GetSection(""Database""));
        services.Configure<CacheOptions>(configuration.GetSection(""Cache""));
        services.Configure<MessagePublishersOptions>(configuration.GetSection(""MessagePublishers""));
        services.Configure<MessageConsumersOptions>(configuration.GetSection(""MessageConsumers""));
        services.Configure<HttpClientOptions>(configuration.GetSection(""HttpClient""));
        services.Configure<GrpcClientOptions>(configuration.GetSection(""GrpcClient""));
        services.Configure<RestClientOptions>(configuration.GetSection(""RestClient""));
        services.Configure<FeatureFlagOptions>(configuration.GetSection(""FeatureFlag""));
        services.Configure<ProfilingOptions>(configuration.GetSection(""Profiling""));
        services.Configure<ObservabilityOptions>(configuration.GetSection(""Observability""));

        return services;
    }}
}}
";
        await WriteFileAsync(Path.Combine(diDir, "ConfigurationExtensions.cs"), configExtContent, dryRun);

        // 5. Copy Endpoints
        var endpointsDir = Path.Combine(workerDir, "Endpoints");
        if (!dryRun) Directory.CreateDirectory(endpointsDir);
        await CopyFileWithRenameAsync(
            Path.Combine(_skeletonPath, "src", "SkeletonApi", "Endpoints", "FeatureFlagEndpoint.cs"),
            Path.Combine(endpointsDir, "FeatureFlagEndpoint.cs"),
            projectName, dryRun);

        // 6. Copy FeatureFlagExtensions
        await CopyFileWithRenameAsync(
            Path.Combine(_skeletonPath, "src", "SkeletonApi", "Extensions", "FeatureFlagExtensions.cs"),
            Path.Combine(diDir, "FeatureFlagExtensions.cs"),
            projectName, dryRun);

        // 7. appsettings.json
        // We can copy from SkeletonApi but trim it down or just create a basic one.
        // MqConsumerGenerator will add MessageBroker settings.
        var appSettingsContent = $@"{{
  ""Logging"": {{
    ""LogLevel"": {{
      ""Default"": ""Information"",
      ""Microsoft.AspNetCore"": ""Warning""
    }}
  }},
  ""AllowedHosts"": ""*"",
  ""Server"": {{
    ""HttpPort"": 4021,
    ""GrpcPort"": 4022,
    ""HttpsPort"": 0,
    ""ShutdownTimeoutSeconds"": 30,
    ""ExcludedLogPaths"": [
      ""/health"",
      ""/health/live"",
      ""/health/ready"",
      ""/hc"",
      ""/grpc.health.v1.Health/Check""
    ],
    ""Cors"": {{
      ""AllowedOrigins"": [ ""*"" ],
      ""AllowedMethods"": [ ""*"" ],
      ""AllowedHeaders"": [ ""*"" ],
      ""AllowCredentials"": false
    }}
  }},
  ""MessagePublishers"": {{
    ""GeneralMQConfig"": {{
      ""rabbitmq"": {{
        ""host"": ""localhost"",
        ""port"": 5672,
        ""username"": ""guest"",
        ""password"": ""guest"",
        ""vhost"": ""/"",
        ""message_ttl"": 2,
        ""queue_expiration"": 3,
        ""enable_dlq"": true,
        ""max_retries"": 5,
        ""queue_type"": ""classic"",
        ""concurrent_consumers"": 5
      }},
      ""kafka"": {{
        ""brokers"": [""localhost:9092""],
        ""client_id"": ""skeleton-api-net-publisher"",
        ""compression"": ""snappy"",
        ""acks"": ""all"",
        ""create_topics"": true
      }},
      ""pubsub"": {{
        ""project_id"": ""your-project-id"",
        ""credentials"": ""credentials/service-account.json""
      }},
      ""mqtt"": {{
        ""broker"": ""tcp://localhost:1883"",
        ""client_id"": ""skeleton-api-net-publisher"",
        ""qos"": 1,
        ""keep_alive_seconds"": 60
      }}
    }},
    ""Topics"": {{}}
  }},
  ""MessageConsumers"": {{
    ""GeneralMQConfig"": {{
      ""rabbitmq"": {{
        ""host"": ""localhost"",
        ""port"": 5672,
        ""username"": ""guest"",
        ""password"": ""guest"",
        ""vhost"": ""/"",
        ""message_ttl"": 2,
        ""queue_expiration"": 3,
        ""enable_dlq"": true,
        ""max_retries"": 5,
        ""queue_type"": ""classic"",
        ""concurrent_consumers"": 5
      }},
      ""kafka"": {{
        ""brokers"": [""localhost:9092""],
        ""group_id"": ""skeleton-api-net-consumer"",
        ""auto_offset_reset"": ""latest"",
        ""create_topics"": true
      }},
      ""pubsub"": {{
        ""project_id"": ""your-project-id"",
        ""credentials"": ""credentials/service-account.json"",
        ""create_subscription"": true
      }},
      ""mqtt"": {{
        ""broker"": ""tcp://localhost:1883"",
        ""client_id"": ""skeleton-api-net-consumer"",
        ""clean_session"": true,
        ""qos"": 1,
        ""keep_alive_seconds"": 60
      }}
    }},
    ""Subscription"": ""worker-service"",
    ""Topics"": {{}}
  }},
  ""FeatureFlag"": {{
    ""Provider"": ""flipt"",
    ""Host"": ""http://localhost:8080"",
    ""Path"": ""config/flags.yaml"",
    ""TimeoutSeconds"": 2,
    ""NamespaceKey"": ""default"",
    ""ClientToken"": """",
    ""Cache"": {{
      ""Enabled"": true,
      ""TtlSeconds"": 60,
      ""RefreshSeconds"": 30,
      ""WarmupFlags"": [
        ""grpc-client""
      ],
      ""MetricsEnabled"": true
    }}
  }}
}}";
        await WriteFileAsync(Path.Combine(workerDir, "appsettings.json"), appSettingsContent, dryRun);

        // appsettings.Development.json
        var devSettingsContent = $@"{{
  ""Server"": {{
    ""CertFile"": ""../../credentials/cert.pem"",
    ""KeyFile"": ""../../credentials/key.pem"",
    ""ShutdownTimeoutSeconds"": 30
  }}
}}";
        await WriteFileAsync(Path.Combine(workerDir, "appsettings.Development.json"), devSettingsContent, dryRun);

        // appsettings.Staging.json & Production.json placeholders
        var placeholderSettings = "{\n}";
        await WriteFileAsync(Path.Combine(workerDir, "appsettings.Staging.json"), placeholderSettings, dryRun);
        await WriteFileAsync(Path.Combine(workerDir, "appsettings.Production.json"), placeholderSettings, dryRun);
        await WriteFileAsync(Path.Combine(workerDir, "appsettings.Regress.json"), placeholderSettings, dryRun);

        // Create Consumers folder
        if (!dryRun) Directory.CreateDirectory(Path.Combine(workerDir, "Consumers"));
    }

    private async Task CopySupportFilesAsync(string targetDir, string projectName, bool dryRun)
    {
        var directories = new[] { "config", "credentials", "deployments", "docs", "migrations", "tests", "tools" };
        foreach (var dir in directories)
        {
            var srcDir = Path.Combine(_skeletonPath, dir);
            if (Directory.Exists(srcDir))
            {
                // For tools, we might want to exclude the generator itself to avoid recursion/bloat
                // But copying HealthCheck is good.
                if (dir == "tools")
                {
                    var healthCheckSrc = Path.Combine(srcDir, "HealthCheck");
                    if (Directory.Exists(healthCheckSrc))
                    {
                        await CopyDirectoryWithRenameAsync(healthCheckSrc, Path.Combine(targetDir, "tools", "HealthCheck"), projectName, dryRun);
                    }
                }
                else
                {
                    await CopyDirectoryWithRenameAsync(srcDir, Path.Combine(targetDir, dir), projectName, dryRun);
                }
            }
        }

        var files = new[]
        {
            ".dockerignore", ".gitignore", "build.sh", "deploy.sh",
            "Jenkinsfile", "Makefile",
            "setup-dependencies.sh", "sonar-project.properties", "README.md"
        };

        foreach (var file in files)
        {
            var srcFile = Path.Combine(_skeletonPath, file);
            if (File.Exists(srcFile))
            {
                await CopyFileWithRenameAsync(srcFile, Path.Combine(targetDir, file), projectName, dryRun);
            }
        }
    }

    private async Task CreateSolutionAsync(string targetDir, string projectName)
    {
        // Detect .NET SDK version — use --format sln for .NET 9+ (including .NET 10) to avoid .slnx
        var sdkVersion = await GetDotNetSdkVersionAsync();
        var formatFlag = sdkVersion >= new Version(9, 0) ? " --format sln" : "";

        // Create SLN
        await RunCommandAsync("dotnet", $"new sln -n \"{projectName}\"{formatFlag}", targetDir);

        // Add projects
        var projects = new[]
        {
            $"src/{projectName}.Common/{projectName}.Common.csproj",
            $"src/{projectName}.Domain/{projectName}.Domain.csproj",
            $"src/{projectName}.Application/{projectName}.Application.csproj",
            $"src/{projectName}.Infrastructure/{projectName}.Infrastructure.csproj",
            $"src/{projectName}/{projectName}.csproj"
        };

        foreach (var proj in projects)
        {
            await RunCommandAsync("dotnet", $"sln add \"{proj}\"", targetDir);
        }
    }

    private async Task<Version> GetDotNetSdkVersionAsync()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process != null)
        {
            var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();

            // Handle versions like "10.0.103" or "10.0.0-preview.7"
            var versionPart = output.Split('-')[0];
            if (Version.TryParse(versionPart, out var version))
            {
                return version;
            }
        }

        return new Version(8, 0); // safe fallback
    }

    // Helper methods (copied/adapted from SmartProjectGenerator)

    private async Task CopyDirectoryWithRenameAsync(string sourceDir, string targetDir, string projectName, bool dryRun, string? entityName = null)
    {
        if (!Directory.Exists(sourceDir)) return;

        if (!dryRun)
        {
            Directory.CreateDirectory(targetDir);
        }

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);
            await CopyFileWithRenameAsync(file, destFile, projectName, dryRun, entityName);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            // Skip bin and obj directories
            if (dirName == "bin" || dirName == "obj") continue;

            var destDir = Path.Combine(targetDir, dirName);
            await CopyDirectoryWithRenameAsync(dir, destDir, projectName, dryRun, entityName);
        }
    }

    private async Task CopyFileWithRenameAsync(string sourceFile, string targetFile, string projectName, bool dryRun, string? entityName = null)
    {
        var content = await File.ReadAllTextAsync(sourceFile);

        // Replace SkeletonApi with ProjectName
        content = content.Replace("SkeletonApi", projectName);

        // Replace skeleton-api-net with project-name-net (kebab case)
        var projectKebab = ToKebabCase(projectName);
        content = content.Replace("skeleton-api-net", projectKebab);

        if (entityName != null)
        {
            content = content.Replace("User", entityName);
            content = content.Replace("user", entityName.ToLower());
        }

        // Rename file if it contains SkeletonApi
        var fileName = Path.GetFileName(targetFile);
        if (fileName.Contains("SkeletonApi"))
        {
            fileName = fileName.Replace("SkeletonApi", projectName);
            targetFile = Path.Combine(Path.GetDirectoryName(targetFile)!, fileName);
        }

        // Special handling for Makefile to ensure solution name matches
        if (fileName == "Makefile")
        {
            content = content.Replace("skeleton-api-net.sln", $"{projectName}.sln");
            content = content.Replace("skeleton-api.sln", $"{projectName}.sln");
            // Also fix the kebab-case replacement if it happened incorrectly for the solution line
            content = content.Replace($"{projectKebab}.sln", $"{projectName}.sln");
        }

        // Special handling for .csproj files to remove Contracts reference
        if (fileName.EndsWith(".csproj"))
        {
            // Remove the Contracts project reference line safely
            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"\s*<ProjectReference Include="".*Contracts.*\.csproj"" />",
                "");

            // Ensure Infrastructure project has reference to Common project (needed for MQ)
            if (fileName.Contains("Infrastructure.csproj"))
            {
                if (!content.Contains($"{projectName}.Common.csproj"))
                {
                    // Look for the last project reference and add it after
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"<ProjectReference Include="".*"" />");
                    if (match.Success)
                    {
                        var commonRef = $"\n    <ProjectReference Include=\"..\\{projectName}.Common\\{projectName}.Common.csproj\" />";
                        content = content.Insert(match.Index + match.Length, commonRef);
                    }
                }
            }
        }

        await WriteFileAsync(targetFile, content, dryRun);
    }

    private async Task WriteFileAsync(string path, string content, bool dryRun)
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

        await File.WriteAllTextAsync(path, content);
        AnsiConsole.MarkupLine($"[green]✓ Generated: {path}[/]");
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

    private string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Regex.Replace(value, "(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z])", "-$1", RegexOptions.Compiled)
            .Trim().ToLower();
    }
}
