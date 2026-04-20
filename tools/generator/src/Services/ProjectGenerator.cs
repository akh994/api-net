using System.IO;
using System.Threading.Tasks;
using Scriban;
using SkeletonApi.Generator.Models;
using Spectre.Console;

namespace SkeletonApi.Generator.Services;

public class ProjectGenerator
{
    public async Task GenerateProjectAsync(EntityDefinition entity, string projectName, string outputDir, bool dryRun)
    {
        var targetDir = string.IsNullOrEmpty(outputDir) ? projectName : outputDir;
        var projectDir = Path.GetFullPath(targetDir);

        AnsiConsole.MarkupLine($"[blue]Generating project: {projectName}[/]");
        AnsiConsole.MarkupLine($"[blue]Entity: {entity.Name}[/]");
        AnsiConsole.MarkupLine($"[blue]Fields: {entity.Fields.Count}[/]");
        AnsiConsole.MarkupLine($"[blue]Methods: {string.Join(", ", entity.Methods)}[/]");

        var files = new Dictionary<string, string>
        {
            // Solution file
            { $"{projectName}.sln", GenerateSolution(projectName) },
            
            // Domain
            { $"src/{projectName}.Domain/{projectName}.Domain.csproj", GenerateDomainCsproj() },
            { $"src/{projectName}.Domain/Entities/{entity.Name}.cs", await GenerateDomainEntity(entity, projectName) },
            
            // Application  
            { $"src/{projectName}.Application/{projectName}.Application.csproj", GenerateApplicationCsproj(projectName) },
            { $"src/{projectName}.Application/Interfaces/I{entity.Name}Repository.cs", await GenerateRepositoryInterface(entity, projectName) },
            { $"src/{projectName}.Application/Interfaces/I{entity.Name}Service.cs", await GenerateServiceInterface(entity, projectName) },
            { $"src/{projectName}.Application/Services/{entity.Name}Service.cs", await GenerateService(entity, projectName) },
            
            // Infrastructure
            { $"src/{projectName}.Infrastructure/{projectName}.Infrastructure.csproj", GenerateInfrastructureCsproj(projectName) },
            { $"src/{projectName}.Infrastructure/Repositories/{entity.Name}Repository.cs", await GenerateRepository(entity, projectName) },
            
            // API
            { $"src/{projectName}/{projectName}.csproj", GenerateApiCsproj(projectName) },
            { $"src/{projectName}/Program.cs", await GenerateProgram(entity, projectName) },
            { $"src/{projectName}/Endpoints/{entity.Name}Endpoints.cs", await GenerateEndpoints(entity, projectName) },
            { $"src/{projectName}/appsettings.json", GenerateAppSettings(projectName) },
            
            // Proto
            { $"proto/{entity.Name.ToLower()}.proto", await GenerateProto(entity) },
            
            // Migrations
            { $"migrations/{DateTime.Now:yyyyMMddHHmmss}_create_{entity.TableName}.up.sql", await GenerateMigrationUp(entity) },
            { $"migrations/{DateTime.Now:yyyyMMddHHmmss}_create_{entity.TableName}.down.sql", GenerateMigrationDown(entity) },
            
            // Makefile
            { "Makefile", GenerateMakefile(projectName) },
            
            // README
            { "README.md", GenerateReadme(projectName, entity) }
        };

        foreach (var (path, content) in files)
        {
            var fullPath = Path.Combine(projectDir, path);

            if (dryRun)
            {
                AnsiConsole.MarkupLine($"[yellow]Would create: {fullPath}[/]");
            }
            else
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await File.WriteAllTextAsync(fullPath, content);
                AnsiConsole.MarkupLine($"[green]Created: {fullPath}[/]");
            }
        }

        if (!dryRun)
        {
            AnsiConsole.MarkupLine($"\n[green]Project generated successfully![/]");
            AnsiConsole.MarkupLine($"[blue]Next steps:[/]");
            AnsiConsole.MarkupLine($"  cd {projectDir}");
            AnsiConsole.MarkupLine($"  dotnet restore");
            AnsiConsole.MarkupLine($"  dotnet build");
        }
    }

    private string GenerateSolution(string projectName) => $@"
Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}"", ""src\{projectName}\{projectName}.csproj"", ""{{00000000-0000-0000-0000-000000000001}}""
EndProject
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}.Domain"", ""src\{projectName}.Domain\{projectName}.Domain.csproj"", ""{{00000000-0000-0000-0000-000000000002}}""
EndProject
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}.Application"", ""src\{projectName}.Application\{projectName}.Application.csproj"", ""{{00000000-0000-0000-0000-000000000003}}""
EndProject
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}.Infrastructure"", ""src\{projectName}.Infrastructure\{projectName}.Infrastructure.csproj"", ""{{00000000-0000-0000-0000-000000000004}}""
EndProject
Global
    GlobalSection(SolutionConfigurationPlatforms) = preSolution
        Debug|Any CPU = Debug|Any CPU
        Release|Any CPU = Release|Any CPU
    EndGlobalSection
EndGlobal
".TrimStart();

    private string GenerateDomainCsproj() => @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";

    private string GenerateApplicationCsproj(string projectName) => $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""..\{projectName}.Domain\{projectName}.Domain.csproj"" />
  </ItemGroup>
</Project>";

    private string GenerateInfrastructureCsproj(string projectName) => $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Dapper"" Version=""2.1.24"" />
    <PackageReference Include=""MySqlConnector"" Version=""2.3.1"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""..\{projectName}.Application\{projectName}.Application.csproj"" />
  </ItemGroup>
</Project>";

    private string GenerateApiCsproj(string projectName) => $@"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""..\{projectName}.Infrastructure\{projectName}.Infrastructure.csproj"" />
  </ItemGroup>
</Project>";

    private async Task<string> GenerateDomainEntity(EntityDefinition entity, string projectName)
    {
        var template = Template.Parse(@"namespace {{ project_name }}.Domain.Entities;

public class {{ name }}
{
{{~ for field in fields ~}}
    public {{ field.type }}{{ if field.nullable }}?{{ end }} {{ field.name }} { get; set; }{{ if !field.nullable }} = {{ if field.type == ""string"" }}string.Empty{{ else if field.type == ""int"" }}0{{ else if field.type == ""bool"" }}false{{ else if field.type == ""DateTime"" }}DateTime.UtcNow{{ else }}default{{ end }};{{ end }}
{{~ end ~}}
}
");
        return await template.RenderAsync(new { project_name = projectName, name = entity.Name, fields = entity.Fields });
    }

    private async Task<string> GenerateRepositoryInterface(EntityDefinition entity, string projectName)
    {
        var template = Template.Parse(@"using {{ project_name }}.Domain.Entities;

namespace {{ project_name }}.Application.Interfaces;

public interface I{{ name }}Repository
{
    Task<IEnumerable<{{ name }}>> GetAllAsync();
    Task<{{ name }}?> GetByIdAsync(string id);
    Task CreateAsync({{ name }} entity);
    Task UpdateAsync({{ name }} entity);
    Task DeleteAsync(string id);
}
");
        return await template.RenderAsync(new { project_name = projectName, name = entity.Name });
    }

    private async Task<string> GenerateServiceInterface(EntityDefinition entity, string projectName)
    {
        var template = Template.Parse(@"using {{ project_name }}.Domain.Entities;

namespace {{ project_name }}.Application.Interfaces;

public interface I{{ name }}Service
{
    Task<IEnumerable<{{ name }}>> GetAllAsync();
    Task<{{ name }}?> GetByIdAsync(string id);
    Task<{{ name }}> CreateAsync({{ name }} entity);
    Task<{{ name }}> UpdateAsync({{ name }} entity);
    Task DeleteAsync(string id);
}
");
        return await template.RenderAsync(new { project_name = projectName, name = entity.Name });
    }

    private async Task<string> GenerateService(EntityDefinition entity, string projectName)
    {
        var template = Template.Parse(@"using {{ project_name }}.Application.Interfaces;
using {{ project_name }}.Domain.Entities;

namespace {{ project_name }}.Application.Services;

public class {{ name }}Service : I{{ name }}Service
{
    private readonly I{{ name }}Repository _repository;

    public {{ name }}Service(I{{ name }}Repository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<{{ name }}>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<{{ name }}?> GetByIdAsync(string id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<{{ name }}> CreateAsync({{ name }} entity)
    {
        entity.Id = Guid.NewGuid().ToString();
        await _repository.CreateAsync(entity);
        return entity;
    }

    public async Task<{{ name }}> UpdateAsync({{ name }} entity)
    {
        await _repository.UpdateAsync(entity);
        return entity;
    }

    public async Task DeleteAsync(string id)
    {
        await _repository.DeleteAsync(id);
    }
}
");
        return await template.RenderAsync(new { project_name = projectName, name = entity.Name });
    }

    private async Task<string> GenerateRepository(EntityDefinition entity, string projectName)
    {
        var template = Template.Parse(@"using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using {{ project_name }}.Application.Interfaces;
using {{ project_name }}.Domain.Entities;
using System.Data;

namespace {{ project_name }}.Infrastructure.Repositories;

public class {{ name }}Repository : I{{ name }}Repository
{
    private readonly string _connectionString;

    public {{ name }}Repository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(""DefaultConnection"") 
            ?? throw new ArgumentNullException(nameof(configuration));
    }

    private IDbConnection CreateConnection() => new MySqlConnection(_connectionString);

    public async Task<IEnumerable<{{ name }}>> GetAllAsync()
    {
        const string sql = ""SELECT * FROM {{ table_name }}"";
        using var connection = CreateConnection();
        return await connection.QueryAsync<{{ name }}>(sql);
    }

    public async Task<{{ name }}?> GetByIdAsync(string id)
    {
        const string sql = ""SELECT * FROM {{ table_name }} WHERE id = @Id"";
        using var connection = CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<{{ name }}>(sql, new { Id = id });
    }

    public async Task CreateAsync({{ name }} entity)
    {
        const string sql = @""
            INSERT INTO {{ table_name }} (id, {{ field_names }})
            VALUES (@Id, {{ field_params }})"";
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, entity);
    }

    public async Task UpdateAsync({{ name }} entity)
    {
        const string sql = @""
            UPDATE {{ table_name }} 
            SET {{ update_fields }}
            WHERE id = @Id"";
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, entity);
    }

    public async Task DeleteAsync(string id)
    {
        const string sql = ""DELETE FROM {{ table_name }} WHERE id = @Id"";
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, new { Id = id });
    }
}
");
        var fieldNames = string.Join(", ", entity.Fields.Where(f => f.Name != "Id").Select(f => ToSnakeCase(f.Name)));
        var fieldParams = string.Join(", ", entity.Fields.Where(f => f.Name != "Id").Select(f => $"@{f.Name}"));
        var updateFields = string.Join(", ", entity.Fields.Where(f => f.Name != "Id").Select(f => $"{ToSnakeCase(f.Name)} = @{f.Name}"));

        return await template.RenderAsync(new
        {
            project_name = projectName,
            name = entity.Name,
            table_name = entity.TableName,
            field_names = fieldNames,
            field_params = fieldParams,
            update_fields = updateFields
        });
    }

    private async Task<string> GenerateProgram(EntityDefinition entity, string projectName)
    {
        var template = Template.Parse(@"using {{ project_name }}.Common.Configuration;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel
builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    var serverConfig = context.Configuration.GetSection(""Server"").Get<ServerOptions>() ?? new ServerOptions();
    serverOptions.ListenAnyIP(serverConfig.HttpPort);
    serverOptions.ListenAnyIP(serverConfig.GrpcPort, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});

// Add services
builder.Services.AddScoped<I{{ name }}Repository, {{ name }}Repository>();
builder.Services.AddScoped<I{{ name }}Service, {{ project_name }}.Application.Services.{{ name }}Service>();

// Configure graceful shutdown timeout
builder.Services.Configure<HostOptions>(options =>
{
    var serverConfig = builder.Configuration.GetSection(""Server"").Get<ServerOptions>() ?? new ServerOptions();
    options.ShutdownTimeout = TimeSpan.FromSeconds(serverConfig.ShutdownTimeoutSeconds > 0 ? serverConfig.ShutdownTimeoutSeconds : 30);
});

var app = builder.Build();

// Configure the HTTP request pipeline
var serverConfig = app.Configuration.GetSection(""Server"").Get<ServerOptions>() ?? new ServerOptions();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        var path = httpContext.Request.Path.Value;
        if (!string.IsNullOrEmpty(path) && serverConfig.ExcludedLogPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            return Serilog.Events.LogEventLevel.Verbose;
        }
        return ex != null || httpContext.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error : Serilog.Events.LogEventLevel.Information;
    };
});

// Map endpoints
app.Map{{ name }}Endpoints().RequireHost($""*:{serverConfig.HttpPort}"");

app.Run();
");
        return await template.RenderAsync(new { project_name = projectName, name = entity.Name });
    }

    private async Task<string> GenerateEndpoints(EntityDefinition entity, string projectName)
    {
        var template = Template.Parse(@"using {{ project_name }}.Application.Interfaces;
using {{ project_name }}.Domain.Entities;

namespace {{ project_name }}.Endpoints;

public static class {{ name }}Endpoints
{
    public static void Map{{ name }}Endpoints(this WebApplication app)
    {
        var group = app.MapGroup(""/api/v1/{{ route_name }}"");

        group.MapGet(""/"", async (I{{ name }}Service service) =>
        {
            var items = await service.GetAllAsync();
            return Results.Ok(items);
        });

        group.MapGet(""/{id}"", async (string id, I{{ name }}Service service) =>
        {
            var item = await service.GetByIdAsync(id);
            return item is not null ? Results.Ok(item) : Results.NotFound();
        });

        group.MapPost(""/"", async ({{ name }} entity, I{{ name }}Service service) =>
        {
            var created = await service.CreateAsync(entity);
            return Results.Created($""/api/v1/{{ route_name }}/{created.Id}"", created);
        });

        group.MapPut(""/{id}"", async (string id, {{ name }} entity, I{{ name }}Service service) =>
        {
            entity.Id = id;
            var updated = await service.UpdateAsync(entity);
            return Results.Ok(updated);
        });

        group.MapDelete(""/{id}"", async (string id, I{{ name }}Service service) =>
        {
            await service.DeleteAsync(id);
            return Results.NoContent();
        });
    }
}
");
        return await template.RenderAsync(new
        {
            project_name = projectName,
            name = entity.Name,
            route_name = entity.TableName
        });
    }

    private string GenerateAppSettings(string projectName) => $@"{{
  ""ConnectionStrings"": {{
    ""DefaultConnection"": ""Server=localhost;Database={projectName.ToLower()};User=root;Password=root;Connection Lifetime=1800;""
  }},
  ""Database"": {{
    ""MaxOpenConnections"": 25,
    ""MaxIdleConnections"": 5,
    ""ConnectionLifetimeSeconds"": 1800
  }},
  ""Cache"": {{
    ""Host"": ""localhost"",
    ""Port"": 6379,
    ""Database"": 2,
    ""User"": ""default"",
    ""Password"": """",
    ""MaxLifetimeSeconds"": 300
  }},
  ""Server"": {{
    ""ShutdownTimeoutSeconds"": 30,
    ""ExcludedLogPaths"": [
      ""/health"",
      ""/health/live"",
      ""/health/ready"",
      ""/hc"",
      ""/grpc.health.v1.Health/Check""
    ]
  }},
  ""Logging"": {{
    ""LogLevel"": {{
      ""Default"": ""Information"",
      ""Microsoft.AspNetCore"": ""Warning""
    }}
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

    private async Task<string> GenerateProto(EntityDefinition entity)
    {
        var template = Template.Parse(@"syntax = ""proto3"";

package {{ name_lower }};

option csharp_namespace = ""{{ name }}.Proto"";

message {{ name }} {
{{~ for field in fields ~}}
  {{ field.proto_type }} {{ field.snake_name }} = {{ for.index + 1 }};
{{~ end ~}}
}

message {{ name }}Response {
  string message = 1;
}

message {{ name }}ListResponse {
  repeated {{ name }} items = 1;
}

message {{ name }}ByIdRequest {
  string id = 1;
}

service {{ name }}GrpcService {
  rpc GetAll(Empty) returns ({{ name }}ListResponse);
  rpc GetById({{ name }}ByIdRequest) returns ({{ name }});
  rpc Create({{ name }}) returns ({{ name }}Response);
  rpc Update({{ name }}) returns ({{ name }}Response);
  rpc Delete({{ name }}ByIdRequest) returns ({{ name }}Response);
}

message Empty {}
");
        var fieldsWithProto = entity.Fields.Select(f => new
        {
            f.Name,
            f.Type,
            snake_name = ToSnakeCase(f.Name),
            proto_type = MapCSharpToProto(f.Type)
        }).ToList();

        return await template.RenderAsync(new
        {
            name = entity.Name,
            name_lower = entity.Name.ToLower(),
            fields = fieldsWithProto
        });
    }

    private async Task<string> GenerateMigrationUp(EntityDefinition entity)
    {
        var template = Template.Parse(@"CREATE TABLE IF NOT EXISTS `{{ table_name }}` (
{{~ for field in fields ~}}
    `{{ field.snake_name }}` {{ field.sql_type }}{{ if !for.last }},{{ end }}
{{~ end ~}}
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
");
        var fieldsWithSql = entity.Fields.Select(f => new
        {
            snake_name = ToSnakeCase(f.Name),
            sql_type = MapCSharpToSql(f.Type, f.Name == "Id")
        }).ToList();

        return await template.RenderAsync(new { table_name = entity.TableName, fields = fieldsWithSql });
    }

    private string GenerateMigrationDown(EntityDefinition entity) =>
        $"DROP TABLE IF EXISTS `{entity.TableName}`;";

    private string GenerateMakefile(string projectName) => $@"
.PHONY: build run test migrate-up migrate-down

# Detect OS and set MAX_CPU_COUNT
MAX_CPU_COUNT ?= 1
ifdef OS
	ifeq ($(OS),Windows_NT)
		MAX_CPU_COUNT = 1
	endif
else
	MAX_CPU_COUNT = $(shell nproc 2>/dev/null || echo 1)
endif

build:
	dotnet build -maxcpucount:$(MAX_CPU_COUNT)


run:
	dotnet run --project src/{projectName}/{projectName}.csproj

test:
	dotnet test

restore:
	dotnet restore
".TrimStart();

    private string GenerateReadme(string projectName, EntityDefinition entity) => $@"# {projectName}

Generated API project with {entity.Name} entity.

## Quick Start

```bash
# Restore dependencies
dotnet restore

# Run the application
dotnet run --project src/{projectName}/{projectName}.csproj
```

## 🪟 Windows Usage Guide

Untuk pengguna Windows, sangat disarankan menggunakan salah satu metode berikut agar pengalaman development tetap *smooth*:

### 1. WSL2 (Windows Subsystem for Linux) - **Sangat Disarankan**
Gunakan WSL2 (Ubuntu 22.04+) untuk menjalankan semua perintah `make` dan script shell.
- **Setup**: Install Ubuntu dari Microsoft Store.
- **VS Code**: Gunakan ekstensi **Remote - WSL** untuk membuka folder project di dalam WSL.
- **Git**: Jalankan `git config --global core.autocrlf input` di Windows agar *line endings* tidak berubah menjadi CRLF.

### 2. DevContainer (Zero Setup)
Jika Anda menggunakan VS Code dan Docker Desktop, Anda bisa langsung membuka project ini di dalam kontainer:
- Klik tombol **""Remote Window""** (ikon hijau di pojok kiri bawah) atau buka Command Palette (`Ctrl+Shift+P`).
- Pilih **""Dev Containers: Reopen in Container""**.
- Semua *dependencies* (.NET SDK, Make) sudah terpasang otomatis di dalam kontainer.

### 3. Docker Desktop
Pastikan Docker Desktop menggunakan **WSL2 Backend** untuk performa terbaik.

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /api/v1/{entity.TableName} | Get all {entity.Name}s |
| GET | /api/v1/{entity.TableName}/{{id}} | Get {entity.Name} by ID |
| POST | /api/v1/{entity.TableName} | Create new {entity.Name} |
| PUT | /api/v1/{entity.TableName}/{{id}} | Update {entity.Name} |
| DELETE | /api/v1/{entity.TableName}/{{id}} | Delete {entity.Name} |

## Database Migration

```bash
# Apply migration
mysql -u root -p < migrations/*_create_{entity.TableName}.up.sql
```
";

    private string ToSnakeCase(string pascalCase) =>
        System.Text.RegularExpressions.Regex.Replace(pascalCase, "([a-z])([A-Z])", "$1_$2").ToLower();

    private string MapCSharpToProto(string csharpType) => csharpType switch
    {
        "string" => "string",
        "int" => "int32",
        "long" => "int64",
        "bool" => "bool",
        "double" => "double",
        "float" => "float",
        "DateTime" => "string",
        _ => "string"
    };

    private string MapCSharpToSql(string csharpType, bool isPrimaryKey) => csharpType switch
    {
        "string" when isPrimaryKey => "VARCHAR(36) NOT NULL PRIMARY KEY",
        "string" => "VARCHAR(255)",
        "int" => "INT",
        "long" => "BIGINT",
        "bool" => "BOOLEAN DEFAULT FALSE",
        "double" => "DOUBLE",
        "float" => "FLOAT",
        "DateTime" => "DATETIME DEFAULT CURRENT_TIMESTAMP",
        _ => "VARCHAR(255)"
    };
}
