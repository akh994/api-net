# Generator Scenarios

This document outlines the various scenarios supported by the `skeleton-api-net` code generator.

## Quick Reference

| Command | Description | Input Types |
|---------|-------------|-------------|
| `project` | Generate complete new project | proto, json, schema, openapi |
| `entity` | Add entity to existing project | proto, json, schema |
| `client` | Generate external service client | grpc, rest, openapi |
| `mq` | Generate MQ publisher | json |
| `consumer` | Generate MQ consumer (add to existing project) | json |
| `new-consumer` | Generate standalone consumer project | json |

### Global Flags for `project` & `entity`

| Flag | Description |
|------|-------------|
| `--update` | Incremental update (wires new methods across all layers) |
| `--only-domain-mapper` | Update only Domain Entities and API Mappers |
| `--only-domain-contracts` | Update only Domain Entities and Proto Contracts |

---

## 1. New Project Generation

Generate a complete microservice project from scratch using different input formats.

### From Proto File (gRPC First) — Recommended

Best when you already have a defined gRPC contract.

**Input (`user.proto`):**
```protobuf
syntax = "proto3";
package user;

import "google/protobuf/wrappers.proto";

message User {
  string id = 1;
  string username = 2;
  google.protobuf.StringValue email = 3;
  string role = 4;
}

service UserGrpcService {
  rpc Add(User) returns (ResUserMessage);
  rpc GetAll(UserEmpty) returns (ResUserAll);
  rpc GetById(UserByIdRequest) returns (User);
  rpc GetByEmail(UserByEmailRequest) returns (User);
  rpc Update(User) returns (ResUserMessage);
  rpc Delete(UserByIdRequest) returns (ResUserMessage);
  rpc Search(UserSearchRequest) returns (ResUserAll);
  rpc GetAllPaginated(PaginationRequest) returns (ResUserPaginated);
}
```

**Command:**
```bash
# Default (MySQL)
./bin/generator project --input user.proto --type proto --name UserApi --output ./UserApi

# PostgreSQL
./bin/generator project --input user.proto --type proto --name UserApi --db postgresql --output ./UserApi
```

> [!TIP]
> **Smart Directory Logic:** Jika nama folder di `--output` sama dengan `--name`, generator akan langsung mengisi folder tersebut. Jika berbeda, generator akan membuat subfolder baru di dalam path tersebut.

**Advanced Features:**
- **Service Parsing:** Parses `service` block RPCs instead of default CRUD methods.
- **Custom Types:** Supports `google.protobuf.StringValue` → `string?` in C#.
- **Complex Types:** Supports `repeated` fields (as `List<T>`) and nested messages.
- **Automatic Methods:** Detects `GetBy*`, `DeleteBy*`, `Search`, `GetAllPaginated`.
- **Standardized Pagination:** Secara otomatis menghasilkan `{Entity}PaginationRequest` dan `{Entity}PaginationMeta` di proto untuk menghindari tabrakan nama (naming collision) antar entitas.
- **Architectural Separation:** Generates Domain POCOs to avoid "Proto Leakage" in Application/Infrastructure layers.

### From JSON Definition

Best for rapid prototyping with custom business logic.

**Input (`product.json`):**
```json
{
  "name": "Product",
  "table_name": "products",
  "fields": [
    {"name": "Name", "type": "string", "nullable": false},
    {"name": "Price", "type": "int", "nullable": false},
    {"name": "Stock", "type": "int", "nullable": false}
  ],
  "methods": ["Add", "GetAll", "GetByID", "Update", "Delete"]
}
```

**Command:**
```bash
./bin/generator project --input product.json --type json --name ProductApi --output ./ProductApi
```

### From SQL Schema (Database First / Reverse Engineering)

Best when migrating legacy databases or starting from a designed schema. Mendukung multiple `CREATE TABLE` dalam satu file SQL untuk menghasilkan project multi-entitas secara utuh.

**Input (`athlete_management.sql`):**
```sql
CREATE TABLE athlete (
  id INT PRIMARY KEY AUTO_INCREMENT,
  name VARCHAR(255) NOT NULL,
  email VARCHAR(255)
);

CREATE TABLE payment (
  id INT PRIMARY KEY AUTO_INCREMENT,
  athlete_id INT,
  amount DECIMAL(10,2),
  payment_date DATETIME
);
```

**Command:**
```bash
# Generate project with all entities detected in SQL
./bin/generator project --input athlete_management.sql --type schema --name AthleteManagement --output ./AthleteManagement
```

> [!IMPORTANT]
> **Multi-Entity Consolidation:** Saat mendeteksi banyak tabel, generator secara otomatis:
> 1. Menghasilkan Domain, Application, Infrastructure layer untuk setiap tabel.
> 2. Menggabungkan registrasi Dependency Injection di `Extensions/DependencyInjection.cs`.
> 3. Mengkonsolidasikan semua endpoint di `Program.cs` sehingga Swagger menampilkan dokumentasi lengkap untuk semua entitas.

### From OpenAPI Specification (YAML/JSON)

Best for API-First development and integrating with existing service definitions.

**Input (`taxi-reservation.json`):**
```json
{
  "openapi": "3.0.0",
  "info": { "title": "Taxi API", "version": "1.0.0" },
  "paths": {
    "/bookings": {
      "post": {
        "operationId": "CreateBooking",
        "requestBody": { ... },
        "responses": { ... }
      }
    }
  }
}
```

**Command:**
```bash
# Generate project including REST client from OpenAPI
./bin/generator project --input api.yaml --type openapi --name TaxiService --output ./TaxiService
```

> [!NOTE]
> When using `type openapi`, the generator automatically creates both the service infrastructure (Clean Architecture) and a fully-wired REST client based on the specification.

### MQ-Only Project (Alias for Standalone Consumer)

For consistency with `skeleton-api-go`, you can also use `project --type mq` as an alias to `new-consumer`:

```bash
# Option 1: Using new-consumer (recommended)
./bin/generator new-consumer --name OrderConsumer --input mq_subscriber.json --output ..

# Option 2: Using project --type mq (alias for Go compatibility)
./bin/generator project --type mq --name OrderConsumer --input mq_subscriber.json --output ..
```

> [!NOTE]
> Both commands are **equivalent** and generate the same standalone Worker Service project. The `new-consumer` command is recommended for clarity, while `project --type mq` provides consistency with the Go generator pattern.

For detailed documentation on standalone consumer projects, see **Section 6: Standalone Consumer Project Generation**.

---

## 2. Entity Generation (Add to Existing Project)

Generate entity files for an existing skeleton-api-net project.

**Command:**
```bash
./bin/generator entity --type proto --input ./proto/user.proto --output .
```

**Flags:**
| Flag | Description |
|------|-------------|
| `--skip-migration` | Skip migration file generation |
| `--skip-proto` | Skip proto file generation |
| `--skip-existing` | Skip files that already exist |
| `--force` | Force overwrite existing files |
| `--backup` | Backup existing files before overwrite |
| `--dry-run` | Preview without writing files |
| `--entity-name` | Override entity name |
| `--table-name` | Override table name |
| `--db` | Database provider (mysql, postgresql, sqlserver) |

**Generated Files:**
```
src/
├── Project.Domain/
│   └── Entities/User.cs              # Domain model
├── Project.Application/
│   ├── Interfaces/IUserRepository.cs # Repository interface
│   ├── Interfaces/IUserUseCase.cs    # UseCase interface
│   └── UseCases/
│       ├── UserUseCase.cs            # UseCase implementation
│       ├── AddUser.cs                # Add use case logic
│       └── ...                       # Other use case files
├── Project.Infrastructure/
│   └── Repositories/
│       ├── UserRepository.cs         # Base repository (uses IDbConnectionFactory)
│       ├── AddUserSql.cs             # Add method SQL
│       └── ...                       # Other method SQL files
├── Project.Contracts/
│   └── Protos/user.proto             # Proto file
└── Project/
    ├── Services/UserGrpcService.cs   # gRPC handlers
    └── Mappers/UserMapper.cs         # Proto ↔ Domain mappers
tests/Project.Tests/                  # Unit tests
migrations/                           # Up/Down SQL migrations
```

---

## 3. Incremental Updates (Method-Level)

Safely update an existing generated project without overwriting manual changes.

### Adding a New Field

1. **Modify Input:** Add new field to proto/JSON.
2. **Run Generator:** Same command as initial generation.
3. **Result:**
   - Domain class: Property injected.
   - Proto message: Field injected.
   - Existing manual code: **Preserved**.

### Adding a New Method (e.g., GetByEmail)

1. **Modify Proto:** Add `rpc GetByEmail(UserByEmailRequest) returns (User);`.
2. **Run Generator:**
   ```bash
   ./bin/generator project --type proto --input ./proto/user.proto --name UserApi --output . --update
   ```
3. **Result (Incremental Injection):**
   - `IUserRepository.cs`: `GetByEmail` method signature injected.
   - `IUserUseCase.cs`: `GetByEmail` method signature injected.
   - `UserGrpcServiceHandler.cs`: `GetByEmail` handler injected.
   - `UserMapper.cs`: `ToProto`/`ToDomain` mappings injected.
   - `UserRepository.cs`: Method implementation with **TODO** injected.

---

## 4. Selective Generation

Update specific layers without touching the rest of the project. This is useful for keeping Domain and Mappers in sync with Proto changes.

### Update Domain and Mappers Only

```bash
./bin/generator project --input user.proto --type proto --name UserApi --output . --only-domain-mapper
```

**What happens:**
- `src/Project.Domain/Entities/*.cs`: Updated based on Proto messages.
- `src/Project/Mappers/*Mapper.cs`: Updated with new field mappings.
- **Safe:** No other files (Services, Repositories, Config) are deleted or modified.

### Update Domain and Contracts Only

```bash
./bin/generator project --input user.proto --type proto --name UserApi --output . --only-domain-contracts
```

**What happens:**
- `src/Project.Domain/Entities/*.cs`: Updated.
- `src/Project.Contracts/Protos/*.proto`: Updated.
- Useful when you want to sync the internal Domain model with the external Contract without updating the API layer yet.

---

## 4. External Client Generation

Generate standalone clients for consuming external services.

### gRPC Client

```bash
./bin/generator client --input user-service.proto --type grpc --name UserService --output .
```

**Generated:**
- `src/Project.Infrastructure/Clients/Grpc/UserServiceClient.cs`
- Auto-injected into dependency injection container.
- Includes Circuit Breaker and Retry patterns.

### REST Client

**Input (`payment-service.json`):**
```json
{
  "service_name": "PaymentService",
  "base_url_config_key": "ExternalServices:Payment:BaseUrl",
  "methods": [
    {
      "name": "CreatePayment",
      "http_method": "POST",
      "path": "/v1/payments",
      "request": {
        "amount": "long",
        "currency": "string"
      },
      "response": {
        "payment_id": "string",
        "status": "string"
      }
    }
  ]
}
```

**Command:**
```bash
./bin/generator client --input payment-service.json --type rest --name PaymentService --output .
```

**Generated:**
- `src/Project.Infrastructure/Clients/Rest/PaymentServiceClient.cs`
- Configuration section injection.
- Circuit Breaker and Retry patterns.

### REST Client from OpenAPI (YAML/JSON)

Automatically generate types and clients from a formal OpenAPI specification.

**Command:**
```bash
./bin/generator client --input petstore.yaml --type openapi --name PetStoreClient --output .
```

**Features:**
- **Schema to Model Mapping**: Automatically generates C# model classes with `JsonPropertyName` attributes.
- **Protobuf Type Handling**: Maps `google.protobuf.Empty` to `void` and `Timestamp` to `DateTime`.
- **Automatic DI Wiring**: Injects both `HttpClient` and `ResilientHttpClient` with observability (APM/Logging).
- **Clean Interface**: Generates strictly typed interfaces for easy mocking.

---

## 5. Message Queue Integration

### MQ Publisher Generation

Generate message publishers for RabbitMQ or Google Cloud Pub/Sub.

**Input (`mq_publisher.json`):**
```json
{
  "broker_type": "rabbitmq",
  "methods": [
    {
      "name": "UserCreated",
      "topic": "user.created",
      "payload": {
        "id": "string",
        "username": "string",
        "email": "string"
      }
    }
  ]
}
```

**Command:**
```bash
./bin/generator mq --input mq_publisher.json --output .
```

**Generated:**
- `src/Project.Infrastructure/Messaging/MqPublisher.cs`
- Payload classes for each event.
- DI registration.

### MQ Consumer Generation (Add to Existing Project)

Generate message consumers for processing incoming events in an **existing project**.

**Input (`mq_subscriber.json`):**
```json
{
  "broker_type": "rabbitmq",
  "methods": [
    {
      "name": "UserCreated",
      "topic": "user.created",
      "subscription": "user.created.service_name",
      "payload": {
        "id": "string",
        "username": "string",
        "email": "string"
      }
    }
  ]
}
```

**Command:**
```bash
./bin/generator consumer --input mq_subscriber.json --output .
```

**Generated:**
- `src/Project/Consumers/UserCreatedConsumer.cs`
- `src/Project.Application/Interfaces/IUserService.cs`
- `src/Project.Application/Services/UserService.cs`
- `src/Project.Application/Models/UserCreatedMessage.cs`
- Automatic wiring in `src/Project/Extensions/DependencyInjection.cs`

---

## 6. Standalone Consumer Project Generation

Generate a **complete standalone Worker Service project** optimized for message consumption without a primary entity.

### Use Case

Best for:
- **Dedicated consumer services** that only process messages
- **Event-driven microservices** without REST/gRPC APIs
- **Background workers** that react to events
- **Decoupled architectures** where consumers are separate from publishers

### Command

```bash
./bin/generator new-consumer --name OrderConsumer --input mq_subscriber.json --output ..
```

### Input Format

**Example (`mq_subscriber.json`):**
```json
{
  "broker_type": "rabbitmq",
  "methods": [
    {
      "name": "UserCreated",
      "topic": "user.created",
      "subscription": "user.created.order_service",
      "payload": {
        "id": "string",
        "username": "string",
        "email": "string",
        "created_at": "string"
      }
    },
    {
      "name": "UserUpdated",
      "topic": "user.updated",
      "subscription": "user.updated.order_service",
      "payload": {
        "id": "string",
        "email": "string",
        "full_name": "string",
        "updated_at": "string"
      }
    },
    {
      "name": "UserDeleted",
      "topic": "user.deleted",
      "subscription": "user.deleted.order_service",
      "payload": {
        "id": "string",
        "deleted_at": "string"
      }
    }
  ]
}
```

### Generated Project Structure

```
OrderConsumer/
├── src/
│   ├── OrderConsumer/                          # Worker Service (Main Project)
│   │   ├── Program.cs                          # Entry point with Serilog, HealthChecks, Profiling
│   │   ├── Consumers/
│   │   │   ├── UserCreatedConsumer.cs          # Consumer for user.created
│   │   │   ├── UserUpdatedConsumer.cs          # Consumer for user.updated
│   │   │   └── UserDeletedConsumer.cs          # Consumer for user.deleted
│   │   ├── Extensions/
│   │   │   ├── ConfigurationExtensions.cs      # Configuration binding
│   │   │   └── DependencyInjection.cs          # Service registration
│   │   └── appsettings.json                    # Configuration with MessageBroker settings
│   ├── OrderConsumer.Application/
│   │   ├── Interfaces/
│   │   │   └── IUserService.cs                 # Business logic interface
│   │   ├── Services/
│   │   │   └── UserService.cs                  # Business logic implementation
│   │   └── Models/
│   │       ├── UserCreatedMessage.cs           # Message DTOs
│   │       ├── UserUpdatedMessage.cs
│   │       └── UserDeletedMessage.cs
│   ├── OrderConsumer.Domain/
│   │   └── Entities/                           # Domain entities (empty initially)
│   ├── OrderConsumer.Infrastructure/
│   │   ├── Messaging/                          # Message broker implementations
│   │   └── Repositories/                       # Data access (if needed)
│   └── OrderConsumer.Common/
│       ├── Configuration/                       # Options classes
│       └── Messaging/                           # RabbitMQ/Kafka/PubSub clients
├── tests/
│   └── OrderConsumer.Tests/                    # Unit tests
├── tools/
│   └── HealthCheck/                            # Health check tool
│       ├── Program.cs
│       └── HealthCheck.csproj
├── config/                                      # Configuration files
├── deployments/                                 # Kubernetes manifests
│   ├── Dockerfile
│   └── service.yaml
├── migrations/                                  # Database migrations (if needed)
├── Makefile                                     # Development commands
├── build.sh                                     # Build script
├── deploy.sh                                    # Deployment script
├── setup-dependencies.sh                        # Dependencies setup
└── OrderConsumer.sln                           # Solution file
```

### Generated Features

#### ✅ Worker Service Configuration
- **Serilog** with `.WriteTo.Console()` for console logging
- **HealthChecks** endpoint at `/health`
- **DiagnosticServer** profiling support (initialization and shutdown handler)
- **Swagger** for API documentation (optional)

#### ✅ Message Broker Support
- **RabbitMQ** with Dead Letter Queue (DLQ)
- **Google Cloud Pub/Sub**
- **Apache Kafka**
- Configurable via `appsettings.json`

#### ✅ Clean Architecture
- **Domain** layer for entities
- **Application** layer for business logic
- **Infrastructure** layer for external dependencies
- **Common** layer for shared utilities

#### ✅ Observability
- **Elastic APM** integration (optional)
- **Structured logging** with Serilog
- **Health endpoints** for Kubernetes probes
- **Profiling** via DiagnosticServer

#### ✅ DevOps Ready
- **Dockerfile** for containerization
- **Kubernetes manifests** for deployment
- **Makefile** with 19 targets for development workflow
- **CI/CD** scripts (build.sh, deploy.sh)

### Running the Generated Project

```bash
cd OrderConsumer

# Build the project
make build

# Run the consumer
make run

# Run with Docker
make docker-build
make docker-run
```

### Configuration

**appsettings.json:**
```json
{
  "MessageBroker": {
    "ClientId": 3,
    "RabbitMQ": {
      "Host": "localhost",
      "Port": 5672,
      "Username": "guest",
      "Password": "guest",
      "MessageTtl": 2,
      "QueueExpiration": 3,
      "EnableDlq": true,
      "ConcurrentConsumers": 5,
      "Topics": {
        "user.created": "user.created",
        "user.updated": "user.updated",
        "user.deleted": "user.deleted"
      },
      "Subscriptions": {
        "user.created": "user.created.order_service",
        "user.updated": "user.updated.order_service",
        "user.deleted": "user.deleted.order_service"
      }
    }
  },
  "Profiling": {
    "Enabled": true,
    "Port": 6060,
    "Host": "localhost"
  }
}
```

### Consumer Implementation Example

**Generated Consumer (`UserCreatedConsumer.cs`):**
```csharp
public class UserCreatedConsumer : IConsumer<UserCreatedMessage>
{
    private readonly IUserService _userService;
    private readonly ILogger<UserCreatedConsumer> _logger;

    public UserCreatedConsumer(IUserService userService, ILogger<UserCreatedConsumer> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    public async Task ConsumeAsync(UserCreatedMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing UserCreated event for user {UserId}", message.Id);
        
        // TODO: Implement business logic
        await _userService.HandleUserCreatedAsync(message);
        
        _logger.LogInformation("Successfully processed UserCreated event for user {UserId}", message.Id);
    }
}
```

### Comparison: `consumer` vs `new-consumer`

| Feature | `consumer` | `new-consumer` |
|---------|------------|----------------|
| **Use Case** | Add consumers to existing project | Create standalone consumer project |
| **Project Structure** | Adds to existing solution | Creates new solution |
| **Dependencies** | Reuses existing layers | Generates all layers (Domain, Application, Infrastructure) |
| **HealthCheck** | Uses existing | Generates new HealthCheck tool |
| **Profiling** | Uses existing | Generates DiagnosticServer support |
| **Configuration** | Updates existing appsettings.json | Generates new appsettings.json |
| **DevOps** | Uses existing Dockerfile/Makefile | Generates new Dockerfile/Makefile |

---

## 7. Redis Cache Generation

Redis cache repository and initialization are **automatically included** in all generated projects by default.

**Generated Files:**
- `src/Project.Infrastructure/Repositories/RedisCacheRepository.cs`: Redis implementation.
- `src/Project.Application/Interfaces/ICacheRepository.cs`: Cache interface.

**Automatic Wiring:**
- **DependencyInjection.cs:** Automatically registers `ICacheRepository` with `RedisCacheRepository` as a Singleton.
- **appsettings.json:** Adds default Redis configuration block.

---

## 8. Unit Test Generation

Automatically included in entity generation.

**Generated (`tests/Project.Tests/UseCases/UserUseCaseTests.cs`):**
- Mock repository using `Moq`.
- Table-driven tests for each method.
- Success and error scenarios.

---

## 9. HealthCheck and Profiling Support

All generated projects (both API and Consumer) include production-ready HealthCheck and Profiling support.

### HealthCheck Tool

**Generated for all projects:**
- `tools/HealthCheck/Program.cs` - Standalone health check executable
- `tools/HealthCheck/HealthCheck.csproj` - Project file

**Usage:**
```bash
# Build health check tool
dotnet build tools/HealthCheck/HealthCheck.csproj

# Run health check
dotnet run --project tools/HealthCheck/HealthCheck.csproj
```

**Kubernetes Integration:**
```yaml
livenessProbe:
  exec:
    command:
    - /app/tools/HealthCheck/HealthCheck
  initialDelaySeconds: 30
  periodSeconds: 10
```

### Profiling Support

**DiagnosticServer** is automatically configured in all generated projects:

**Program.cs (Auto-generated):**
```csharp
// Initialize diagnostic server if enabled
ProjectName.Common.Diagnostics.DiagnosticServer? diagnosticServer = null;
var profilingOptions = builder.Configuration.GetSection("Profiling").Get<ProfilingOptions>();
if (profilingOptions?.Enabled == true)
{
    var diagnosticLogger = app.Services.GetRequiredService<ILogger<ProjectName.Common.Diagnostics.DiagnosticServer>>();
    diagnosticServer = new ProjectName.Common.Diagnostics.DiagnosticServer(
        profilingOptions.Host,
        profilingOptions.Port,
        diagnosticLogger
    );
    
    _ = diagnosticServer.StartAsync();
    
    Log.Information("Diagnostic profiling enabled at {Address}", 
        $"http://{profilingOptions.Host}:{profilingOptions.Port}/debug/diagnostics/");
}

// Shutdown handler
if (diagnosticServer != null)
{
    var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    appLifetime.ApplicationStopping.Register(() =>
    {
        diagnosticServer.StopAsync().GetAwaiter().GetResult();
    });
}
```

**Configuration (appsettings.json):**
```json
{
  "Profiling": {
    "Enabled": true,
    "Port": 6060,
    "Host": "localhost"
  }
}
```

**Access Diagnostics:**
```bash
# Memory dump
curl http://localhost:6060/debug/diagnostics/dump

# GC stats
curl http://localhost:6060/debug/diagnostics/gc

# Thread dump
curl http://localhost:6060/debug/diagnostics/threads
```

---

## 10. Best Practices

1. **Start with Proto:** Define your API contract in proto files first.
2. **Use PascalCase:** .NET conventions prefer PascalCase for project and entity names.
3. **Incremental Updates:** Add methods one at a time and verify.
4. **Manual Code:** The generator is designed to preserve manual changes in implementation files.
5. **Review Changes:** Use `--dry-run` to preview before committing.

---

## 11. Troubleshooting

| Issue | Solution |
|-------|----------|
| Duplicate code injected | Check if injection markers (e.g., `// [INJECTION POINT]`) exist. |
| Missing namespaces | Ensure the `projectName` matches your solution namespace. |
| Build errors | Run `dotnet restore` and `dotnet build` to resolve dependencies. |
| Tests fail | Verify that mock setups in tests match the new interface signatures. |

## Cheatsheet

1. make gen ARGS="project --input examples/mq_subscriber.json --type mq --name UserEvent --output ../UserEvent"
2. make gen ARGS="project --input examples/user.proto --name UserSerice --output ../UserService"
3. make gen ARGS="project --input examples/taxi-reservation.yaml --type openapi --name TaxiReservation --output ../TaxiReservation"
4. make gen ARGS="project --input examples/taxi-reservation.json --type openapi --name TaxiReservationJson --output ../TaxiReservationJson"
5. make gen ARGS="project --input examples/athlete_management.sql --type schema --name AthleteManagement --output ../AthleteManagement"