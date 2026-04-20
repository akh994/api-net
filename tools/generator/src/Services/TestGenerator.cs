using System.Text;
using SkeletonApi.Generator.Models;

namespace SkeletonApi.Generator.Services;

public class TestGenerator
{
    public string GenerateValidatorTests(EntityDefinition entity, string projectName)
    {
        return $@"using FluentAssertions;
using AppValidators = global::{projectName}.Application.Validators;
using DomainModels = global::{projectName}.Domain.Entities;
using Xunit;

namespace {projectName}.Tests.Unit.Validators;

public class {entity.Name}ValidatorTests
{{
    private readonly AppValidators.{entity.Name}Validator _validator;

    public {entity.Name}ValidatorTests()
    {{
        _validator = new AppValidators.{entity.Name}Validator();
    }}

    [Fact]
    public void Validate_ShouldBeValid_WhenEntityIsCorrect()
    {{
        // Arrange
        var entity = new DomainModels.{entity.Name}
        {{
{string.Join(",\n", entity.Fields.Where(f => f.Type == "string").Select(f => $"            {f.Name} = \"test-{f.Name.ToLower()}\""))}
        }};
        
        // Act
        var result = _validator.Validate(entity);

        // Assert
        result.IsValid.Should().BeTrue();
    }}
}}";
    }

    public string GenerateCacheTests(EntityDefinition entity, string projectName)
    {
        var idType = GetIdType(entity).ToLower();
        var testIdValue = (idType == "int" || idType == "long") ? "1" : "\"test-id\"";

        return $@"using System.Text.Json;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using {projectName}.Infrastructure.Repositories;
using DomainEntities = global::{projectName}.Domain.Entities;
using Xunit;

namespace {projectName}.Tests.Unit.Repositories;

public class RedisCacheRepositoryTests
{{
    private readonly Mock<IConnectionMultiplexer> _mockRedisMaster;
    private readonly Mock<IConnectionMultiplexer> _mockRedisReplica;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly RedisCacheRepository _cacheRepository;

    public RedisCacheRepositoryTests()
    {{
        _mockRedisMaster = new Mock<IConnectionMultiplexer>();
        _mockRedisReplica = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();

        _mockRedisMaster.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_mockDatabase.Object);
        _mockRedisReplica.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_mockDatabase.Object);

        _cacheRepository = new RedisCacheRepository(_mockRedisMaster.Object, _mockRedisReplica.Object);
    }}

    [Fact]
    public async Task GetAsync_ShouldReturnData_WhenKeyExists()
    {{
        // Arrange
        var key = ""test-key"";
        var entity = new DomainEntities.{entity.Name} {{ Id = {testIdValue} }};
        var json = JsonSerializer.Serialize(entity);
        
        _mockDatabase.Setup(d => d.StringGetAsync(key, It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        // Act
        var result = await _cacheRepository.GetAsync<DomainEntities.{entity.Name}>(key);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(entity.Id);
    }}

    [Fact]
    public async Task SetAsync_ShouldCallStringSet()
    {{
        // Arrange
        var key = ""test-key"";
        var entity = new DomainEntities.{entity.Name} {{ Id = {testIdValue} }};

        // Act
        await _cacheRepository.SetAsync(key, entity);

        // Assert
        _mockDatabase.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), 
            It.IsAny<RedisValue>(), 
            It.IsAny<TimeSpan?>(), 
            It.IsAny<bool>(), 
            It.IsAny<When>(), 
            It.IsAny<CommandFlags>()), Times.Once);
    }}
}}";
    }

    public string GenerateServiceTests(EntityDefinition entity, string projectName)
    {
        var entityNameLower = entity.Name.ToLower();
        var idType = GetIdType(entity);
        var tests = new List<string>();

        foreach (var method in entity.Methods)
        {
            var methodName = method.Name;

            if (methodName.Equals("GetById", StringComparison.OrdinalIgnoreCase))
            {
                tests.Add($@"    [Fact]
    public async Task GetByIdAsync_ShouldReturnEntity_WhenExists()
    {{
        // Arrange
        var id = {(idType == "int" || idType == "long" ? "1" : "\"test-id\"")};
        var entity = new DomainEntities.{entity.Name} {{ Id = id }};
        _mockRepository.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        // Act
        var result = await _service.GetByIdAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
    }}");
            }
            else if (methodName.Equals("GetAll", StringComparison.OrdinalIgnoreCase))
            {
                tests.Add($@"    [Fact]
    public async Task GetAllAsync_ShouldReturnList()
    {{
        // Arrange
        var items = new List<DomainEntities.{entity.Name}> {{ new DomainEntities.{entity.Name} {{ Id = {(idType == "int" || idType == "long" ? "1" : "\"1\"")} }} }};
        _mockRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(items);

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
    }}");
            }
            else if (!methodName.Equals("Add", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("Create", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("Update", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("Delete", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("Search", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("GetAllPaginated", StringComparison.OrdinalIgnoreCase))
            {
                // Custom method test generation
                var inputDomainType = GetDomainType(method.RequestType);
                var outputDomainType = GetDomainType(method.ResponseType);
                var isVoid = outputDomainType == "void";
                var inputParam = inputDomainType == "void" ? "" :
                    (inputDomainType == "string" || inputDomainType == "string?" ? "\"test\"" :
                    (inputDomainType == "int" || inputDomainType == "int?" || inputDomainType == "long" || inputDomainType == "long?" ? "1" :
                    (inputDomainType == "bool" || inputDomainType == "bool?" ? "true" :
                    (inputDomainType == "DateTime" || inputDomainType == "DateTime?" ? "DateTime.UtcNow" :
                    (inputDomainType == "double" || inputDomainType == "double?" || inputDomainType == "float" || inputDomainType == "float?" ? "1.0" :
                    $"new DomainEntities.{inputDomainType}()")))));

                tests.Add($@"    [Fact]
    public async Task {methodName}Async_ShouldCallRepository()
    {{
        // Arrange
        // Mock repository default return if needed
        {(isVoid ? "" : $"// _mockRepository.Setup(r => r.{methodName}Async(It.IsAny<{inputDomainType}>())).ReturnsAsync(new {outputDomainType}());")}

        // Act
        Func<Task> act = async () => await _service.{methodName}Async({inputParam});

        // Assert
        await act.Should().NotThrowAsync();
    }}");
            }
        }

        return $@"using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Moq;
using {projectName}.Application.Interfaces;
using AppServices = global::{projectName}.Application.Services;
using DomainEntities = global::{projectName}.Domain.Entities;
using Xunit;

namespace {projectName}.Tests.Unit.Services;

public class {entity.Name}ServiceTests
{{
    private readonly Mock<I{entity.Name}Repository> _mockRepository;
    private readonly Mock<ICacheRepository> _mockCacheRepository;
    private readonly Mock<ILogger<AppServices.{entity.Name}Service>> _mockLogger;
    private readonly Mock<IValidator<DomainEntities.{entity.Name}>> _mockValidator;
    private readonly Mock<global::{projectName}.Common.Concurrency.ISingleFlight> _mockSingleFlight;
    private readonly AppServices.{entity.Name}Service _service;

    public {entity.Name}ServiceTests()
    {{
        _mockRepository = new Mock<I{entity.Name}Repository>();
        _mockCacheRepository = new Mock<ICacheRepository>();
        _mockLogger = new Mock<ILogger<AppServices.{entity.Name}Service>>();
        _mockValidator = new Mock<IValidator<DomainEntities.{entity.Name}>>();
        _mockSingleFlight = new Mock<global::{projectName}.Common.Concurrency.ISingleFlight>();

        _service = new AppServices.{entity.Name}Service(
            _mockRepository.Object,
            _mockCacheRepository.Object,
            _mockLogger.Object,
            _mockValidator.Object,
            _mockSingleFlight.Object
        );
    }}

{string.Join("\n\n", tests)}
}}";
    }

    public string GenerateRepositoryTests(EntityDefinition entity, string projectName)
    {
        var idType = GetIdType(entity).ToLower();
        var testIdValue = (idType == "int" || idType == "long") ? "1" : "\"test-id\"";
        var tests = new List<string>();

        foreach (var method in entity.Methods)
        {
            var methodName = method.Name;

            if (methodName.Equals("GetById", StringComparison.OrdinalIgnoreCase))
            {
                tests.Add($@"    [Fact]
    public async Task GetByIdAsync_ShouldReturnEntity_WhenExists()
    {{
        // Arrange
        var id = {testIdValue};
        var expectedEntity = new DomainEntities.{entity.Name} {{ Id = id }};
        
        _mockConnection.SetupDapperAsync(c => c.QueryAsync<DomainEntities.{entity.Name}>(
            It.IsAny<string>(), 
            It.IsAny<object>(), 
            null, null, null))
            .ReturnsAsync(new List<DomainEntities.{entity.Name}> {{ expectedEntity }});

        // Act
        var result = await _repository.GetByIdAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
    }}");
            }
            else if (methodName.Equals("GetAll", StringComparison.OrdinalIgnoreCase))
            {
                tests.Add($@"    [Fact]
    public async Task GetAllAsync_ShouldReturnList()
    {{
        // Arrange
        var items = new List<DomainEntities.{entity.Name}> {{ new DomainEntities.{entity.Name} {{ Id = {testIdValue} }} }};
        _mockConnection.SetupDapperAsync(c => c.QueryAsync<DomainEntities.{entity.Name}>(
            It.IsAny<string>(), 
            It.IsAny<object>(), 
            null, null, null))
            .ReturnsAsync(items);

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
    }}");
            }
            else if (!methodName.Equals("Add", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("Create", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("Update", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("Delete", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("Search", StringComparison.OrdinalIgnoreCase) &&
                     !methodName.Equals("GetAllPaginated", StringComparison.OrdinalIgnoreCase))
            {
                // Custom method test generation
                var inputDomainType = GetDomainType(method.RequestType);
                var outputDomainType = GetDomainType(method.ResponseType);
                var isVoid = outputDomainType == "void";
                var inputParam = inputDomainType == "void" ? "" :
                    (inputDomainType == "string" || inputDomainType == "string?" ? "\"test\"" :
                    (inputDomainType == "int" || inputDomainType == "int?" || inputDomainType == "long" || inputDomainType == "long?" ? "1" :
                    (inputDomainType == "bool" || inputDomainType == "bool?" ? "true" :
                    (inputDomainType == "DateTime" || inputDomainType == "DateTime?" ? "DateTime.UtcNow" :
                    (inputDomainType == "double" || inputDomainType == "double?" || inputDomainType == "float" || inputDomainType == "float?" ? "1.0" :
                    $"new DomainEntities.{inputDomainType}()")))));

                tests.Add($@"    [Fact]
    public async Task {methodName}Async_ShouldNotThrow()
    {{
        // Arrange
        // Setup mock if needed based on return type
        {(isVoid ? "" : $"// _mockConnection.SetupDapperAsync(...)")}

        // Act
        Func<Task> act = async () => await _repository.{methodName}Async({inputParam});

        // Assert
        await act.Should().NotThrowAsync();
    }}");
            }
        }

        return $@"using System.Data;
using Dapper;
using Moq;
using Moq.Dapper;
using FluentAssertions;
using {projectName}.Application.Interfaces;
using {projectName}.Infrastructure.Interfaces;
using InfraRepos = global::{projectName}.Infrastructure.Repositories;
using DomainEntities = global::{projectName}.Domain.Entities;
using Xunit;

namespace {projectName}.Tests.Unit.Repositories;

public class {entity.Name}RepositoryTests
{{
    private readonly Mock<IDbConnectionFactory> _mockDbConnectionFactory;
    private readonly Mock<IDbConnection> _mockConnection;
    private readonly InfraRepos.{entity.Name}Repository _repository;

    public {entity.Name}RepositoryTests()
    {{
        _mockConnection = new Mock<IDbConnection>();
        _mockDbConnectionFactory = new Mock<IDbConnectionFactory>();
        _mockDbConnectionFactory.Setup(x => x.CreateConnection())
            .Returns(_mockConnection.Object);

        _repository = new InfraRepos.{entity.Name}Repository(_mockDbConnectionFactory.Object);
    }}

{string.Join("\n\n", tests)}
}}";
    }

    private string GetIdType(EntityDefinition entity)
    {
        var pk = entity.Fields.Find(f => f.PrimaryKey);
        if (pk != null) return pk.Type;

        var idField = entity.Fields.Find(f => f.Name.Equals("Id", StringComparison.OrdinalIgnoreCase));
        return idField?.Type ?? "string";
    }

    private string GetDomainType(string protoType)
    {
        if (protoType == "Empty" || protoType == "google.protobuf.Empty")
        {
            return "void";
        }

        if (protoType.StartsWith("google.protobuf."))
        {
            return protoType switch
            {
                "google.protobuf.Timestamp" => "DateTime",
                "google.protobuf.StringValue" => "string?",
                "google.protobuf.Int32Value" => "int?",
                "google.protobuf.Int64Value" => "long?",
                "google.protobuf.BoolValue" => "bool?",
                "google.protobuf.DoubleValue" => "double?",
                "google.protobuf.FloatValue" => "float?",
                _ => "string"
            };
        }
        // It's already a domain type or a primitive
        return protoType;
    }
}
