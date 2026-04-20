using Dapper;
using Elastic.Apm.NetCoreAll;
using Elastic.Apm.SerilogEnricher;
using Microsoft.OpenApi.Models;
using Prometheus;
using Serilog;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Common.Extensions;
using SkeletonApi.Common.Logging;
using SkeletonApi.Common.Middleware;
using SkeletonApi.Endpoints;
using SkeletonApi.Extensions;
using SkeletonApi.Services;

DefaultTypeMap.MatchNamesWithUnderscores = true;

// Bootstrap config early so we can read values before builder is fully initialized
var bootstrapConfig = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Map Observability:ElasticApm → ELASTIC_APM_* env vars so the agent picks them up
// (must be done BEFORE WebApplication.CreateBuilder to take effect)
var apmSection = bootstrapConfig.GetSection("Observability:ElasticApm");
if (!string.IsNullOrEmpty(apmSection["ServiceName"]))
    Environment.SetEnvironmentVariable("ELASTIC_APM_SERVICE_NAME", apmSection["ServiceName"]);
if (!string.IsNullOrEmpty(apmSection["ServerUrl"]))
    Environment.SetEnvironmentVariable("ELASTIC_APM_SERVER_URL", apmSection["ServerUrl"]);
if (!string.IsNullOrEmpty(apmSection["ServiceVersion"]))
    Environment.SetEnvironmentVariable("ELASTIC_APM_SERVICE_VERSION", apmSection["ServiceVersion"]);
if (!string.IsNullOrEmpty(apmSection["Environment"]))
    Environment.SetEnvironmentVariable("ELASTIC_APM_ENVIRONMENT", apmSection["Environment"]);
if (!string.IsNullOrEmpty(apmSection["SecretToken"]))
    Environment.SetEnvironmentVariable("ELASTIC_APM_SECRET_TOKEN", apmSection["SecretToken"]);
if (!string.IsNullOrEmpty(apmSection["LogLevel"]))
    Environment.SetEnvironmentVariable("ELASTIC_APM_LOG_LEVEL", apmSection["LogLevel"]);

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use ports from appsettings
builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    var serverConfig = context.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();

    // HTTP port for REST API
    serverOptions.ListenAnyIP(serverConfig.HttpPort);

    // gRPC port
    serverOptions.ListenAnyIP(serverConfig.GrpcPort, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });

    // HTTPS port (if configured with certificates)
    if (serverConfig.HttpsPort > 0 && !string.IsNullOrEmpty(serverConfig.CertFile))
    {
        serverOptions.ListenAnyIP(serverConfig.HttpsPort, listenOptions =>
        {
            // Support both PFX and PEM formats
            if (!string.IsNullOrEmpty(serverConfig.KeyFile))
            {
                // Separate PEM files (cert + key) - Load and combine
                listenOptions.UseHttps(httpsOptions =>
                {
                    var cert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(
                        serverConfig.CertFile,
                        serverConfig.KeyFile);
                    httpsOptions.ServerCertificate = cert;
                });
            }
            else
            {
                // Single PFX file
                listenOptions.UseHttps(serverConfig.CertFile);
            }
        });
        Log.Information("HTTPS server configured on port {HttpsPort}", serverConfig.HttpsPort);
    }
});

// Configure Serilog using the modern lambda approach
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithElasticApmCorrelationInfo()
    .Enrich.With(new CustomLoggerEnricher()) // Use inline enricher defined at end of file
    .Enrich.With(new CallerEnricher())
    .WriteTo.Console(outputTemplate: "[{Skel_Timestamp}] [{Level:u3}] [{Skel_TraceId}] [{CallerFile}:{CallerLine}] {Message:lj}{NewLine}{Exception}")
);

// Add services to the container
builder.Services.AddControllers(); // For SSE endpoint
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcGlobalExceptionInterceptor>();
}).AddJsonTranscoding();
builder.Services.AddGrpcReflection();

// Add Elastic APM
builder.Services.AddAllElasticApm();


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddGrpcSwagger();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Skeleton API", Version = "v1" });

    // Add JWT Authentication support to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Bind configuration options
builder.Services.AddConfigurationOptions(builder.Configuration);

// Configure graceful shutdown timeout
builder.Services.Configure<HostOptions>(options =>
{
    var serverConfig = builder.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
    options.ShutdownTimeout = TimeSpan.FromSeconds(serverConfig.ShutdownTimeoutSeconds > 0 ? serverConfig.ShutdownTimeoutSeconds : 30);
});

// Register application services via extension methods
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddHealthChecksConfiguration(builder.Configuration);
builder.Services.AddCommonCors(builder.Configuration);

// Register feature flag services and initialize provider
builder.Services.AddFeatureFlagServices(builder.Configuration);

var app = builder.Build();

// Log application lifecycle events
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

lifetime.ApplicationStopping.Register(() =>
{
    Log.Information("Application is stopping - waiting for in-flight requests to complete");
});

lifetime.ApplicationStopped.Register(() =>
{
    Log.Information("Application stopped gracefully");
});

// Initialize diagnostic server if enabled
SkeletonApi.Common.Diagnostics.DiagnosticServer? diagnosticServer = null;
var profilingOptions = builder.Configuration.GetSection("Profiling").Get<ProfilingOptions>();
if (profilingOptions?.Enabled == true)
{
    var diagnosticLogger = app.Services.GetRequiredService<ILogger<SkeletonApi.Common.Diagnostics.DiagnosticServer>>();
    diagnosticServer = new SkeletonApi.Common.Diagnostics.DiagnosticServer(
        profilingOptions.Host,
        profilingOptions.Port,
        diagnosticLogger
    );

    _ = diagnosticServer.StartAsync();

    Log.Information("Diagnostic profiling enabled at {Address}",
        $"http://{profilingOptions.Host}:{profilingOptions.Port}/debug/diagnostics/");
}
else
{
    Log.Information("Diagnostic profiling disabled");
}

// Configure the HTTP request pipeline
var serverConfig = builder.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapGrpcReflectionService();
}

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        var path = httpContext.Request.Path.Value;
        if (!string.IsNullOrEmpty(path) && serverConfig.ExcludedLogPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            return Serilog.Events.LogEventLevel.Verbose; // Skip logging by setting to Verbose (default level is Information)
        }
        return ex != null || httpContext.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error : Serilog.Events.LogEventLevel.Information;
    };
});

app.UseCors("AllowAll");

app.UseAuthentication();

// Extract claims from headers
app.UseMiddleware<ClaimsMiddleware>();

// Wrap REST API responses with standardized format
app.UseApiResponseWrapper();

// app.UseHttpsRedirection(); // Disabled to allow independent HTTP and HTTPS ports

// Map gRPC services
app.MapGrpcService<UserGrpcService>();

// Map Health Check endpoints - accessible on all ports for Kubernetes probes
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // No checks, just returns 200 if app is running
});

// Map Controllers (for SSE endpoint) - Restricted to HttpPort
app.MapControllers().RequireHost($"*:{serverConfig.HttpPort}");

// Map Feature Flags endpoint - Restricted to HttpPort
app.MapFeatureFlagEndpoint(serverConfig.HttpPort);

// Map Prometheus metrics endpoint - Restricted to HttpPort
app.MapMetrics().RequireHost($"*:{serverConfig.HttpPort}");


app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909")
    .RequireHost($"*:{serverConfig.HttpPort}");

// Register shutdown handler for diagnostic server
if (diagnosticServer != null)
{
    var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    appLifetime.ApplicationStopping.Register(() =>
    {
        diagnosticServer.StopAsync().GetAwaiter().GetResult();
    });
}

app.Run();

// Custom enricher to ensure it's always in sync with Program.cs and correctly formatted.
// This handles W3C Trace IDs and ISO 8601 UTC timestamps.
public class CustomLoggerEnricher : Serilog.Core.ILogEventEnricher
{
    public void Enrich(Serilog.Events.LogEvent logEvent, Serilog.Core.ILogEventPropertyFactory propertyFactory)
    {
        var activity = System.Diagnostics.Activity.Current;
        // Use standard Activity.Id (W3C format) if available, otherwise fallback.
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("Skel_TraceId", activity?.Id ?? "no-trace"));

        // Ensure UTC ISO 8601 formatting for timestamps.
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("Skel_Timestamp", logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")));
    }
}
