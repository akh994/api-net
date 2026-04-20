# PlantUML Sequence Diagrams

This directory contains PlantUML sequence diagrams that document the flow of operations in the skeleton-api-net application.

## Available Diagrams

### 1. Add User and User Created Consumer Flow (`add-user-sequence.puml`)

**Purpose**: Comprehensive sequence diagram showing the complete flow of adding a user and processing the user created event through the message consumer in .NET 10.

**Key Features**:
- **Clean Architecture Layers**: Shows interaction between Presentation, Application, Domain, and Infrastructure layers
- **Infrastructure Components**: MySQL (Dapper), Redis, RabbitMQ, External Services
- **Cross-Cutting Concerns**: FluentValidation, Serilog, Elastic APM, UserMapper, FeatureFlagService, SingleFlight
- **Distributed Tracing**: W3C Trace Context propagation using Activity.Current
- **Feature Flags**: Conditional routing between gRPC and REST clients using OpenFeature
- **Best Effort Operations**: Cache and message publishing with error handling

**Flow Overview**:

1. **Add User Flow**:
   - Client sends gRPC request → UserGrpcService
   - Service converts proto to domain → UserService
   - UserService validates with FluentValidation
   - Generates GUID and timestamps
   - Checks email uniqueness
   - Saves to MySQL using Dapper
   - Caches in Redis (best effort)
   - Publishes to RabbitMQ with trace context (best effort)
   - Returns response to client

2. **User Created Consumer Flow**:
   - Consumer (BackgroundService) receives message from RabbitMQ
   - Extracts trace context from headers (Activity.Current)
   - Creates scoped DI container
   - Deserializes message
   - Calls UserService.ProcessUserCreatedAsync()
   - Checks feature flag via OpenFeature
   - Fetches user via gRPC or REST client
   - Publishes SSE event to Redis (best effort)
   - Acknowledges message

3. **SSE Stream Users Flow**:
   - Client connects to HTTP GET /api/v1/users/stream
   - SseEndpoint sets SSE headers and generates clientId
   - Sends connected event
   - Fetches initial user list via UserService.GetAllAsync()
   - Sends initial data to client immediately
   - Registers client with SseManager
   - Keeps connection open until client disconnects

4. **Real-time Update via SSE**:
   - Redis Pub/Sub receives event
   - SseManager's SubscribeToRedisAsync receives message
   - Deserializes to SseEvent
   - Broadcasts event to all connected clients
   - Each client receives update via delegate
   - HTTP response flushes event to client

**Components Involved**:

| Layer | Components |
|-------|-----------|
| **Presentation** | UserGrpcService, SseEndpoint, UserCreatedConsumer |
| **Application** | UserService (IUserService), UserValidator |
| **Domain** | User (Entity) |
| **Infrastructure** | UserRepository (Dapper), CacheRepository, UserMessagePublisher, UserGrpcRepository, UserRestRepository, SseManager |
| **Infrastructure** | MySQL Database, Redis Cache, RabbitMQ, External Service |
| **Cross-Cutting** | FluentValidation, UserMapper, Elastic APM, Serilog, FeatureFlagService, SingleFlight |

**Technology Stack (.NET 10)**:

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Framework | .NET 10.0 | Application framework |
| API | gRPC + JSON Transcoding | Dual protocol support |
| ORM | Dapper | High-performance micro-ORM |
| Caching | StackExchange.Redis | Distributed caching |
| Validation | FluentValidation | Input validation |
| Logging | Serilog | Structured logging |
| APM | Elastic.Apm.NetCoreAll | Distributed tracing |
| Resilience | Polly | Circuit breaker & retry |
| Feature Flags | OpenFeature | Feature management |
| Message Broker | RabbitMQ.Client / Kafka / Pub/Sub | Async messaging |
| DI | Microsoft.Extensions.DependencyInjection | Built-in DI |

**Distributed Tracing**:
- Uses Elastic APM with W3C Trace Context standard
- Trace ID propagated via `traceparent` header in message metadata
- Activity.Current provides automatic trace context
- Consumer transaction linked to original request transaction
- End-to-end visibility: Request → Publish → Consume → Process

## How to Use

### View Diagrams

**Option 1: VS Code with PlantUML Extension**
1. Install "PlantUML" extension
2. Open `.puml` file
3. Press `Alt+D` to preview

**Option 2: Online Viewer**
1. Copy content of `.puml` file
2. Go to http://www.plantuml.com/plantuml/uml/
3. Paste and view

**Option 3: Generate PNG/SVG**
```bash
# Install PlantUML
sudo apt install plantuml

# Generate PNG
plantuml docs/add-user-sequence.puml

# Generate SVG
plantuml -tsvg docs/add-user-sequence.puml
```

### Generate Code from Diagram

This diagram serves as a **blueprint for code generation**. When implementing new features:

1. **Identify the layers**: Presentation → Application → Infrastructure
2. **Follow the pattern**: 
   - Presentation layer handles protocol (gRPC/HTTP) - inherits from gRPC service base or ASP.NET Controller
   - Application layer contains business logic (services with interfaces)
   - Infrastructure layer handles data access and external communication
3. **Include cross-cutting concerns**:
   - FluentValidation for input validation
   - Serilog for structured logging
   - Elastic APM for distributed tracing
   - Error handling with try-catch and RpcException
4. **Implement distributed tracing**:
   - Use Activity.Current for trace context
   - Propagate trace context via headers
   - Link related transactions

### Example: Adding a New Feature

To add a "Delete User" feature with event publishing:

1. **Presentation Layer**: Add method to `UserGrpcService.cs`
   ```csharp
   public override async Task<ResUserMessage> Delete(UserByIdRequest request, ServerCallContext context)
   {
       await _userService.DeleteAsync(request.Id);
       return new ResUserMessage { Message = "User deleted successfully" };
   }
   ```

2. **Application Layer**: Implement in `UserService.cs`
   ```csharp
   public async Task DeleteAsync(string id)
   {
       await _userRepository.DeleteAsync(id);
       await _cacheRepository.RemoveAsync($"user:{id}");
       if (_messagePublisher != null)
       {
           await _messagePublisher.PublishUserDeletedAsync(id);
       }
   }
   ```

3. **Infrastructure Layer**: 
   - Add `DeleteAsync(id)` in UserRepository (Dapper)
   - Add `PublishUserDeletedAsync(id)` in UserMessagePublisher
   
4. **Consumer**: Create `UserDeletedConsumer.cs` following BackgroundService pattern

5. **Update Diagram**: Create `delete-user-sequence.puml` based on this template

## Diagram Conventions

- **Boxes**: Group related components by layer/concern
- **Colors**: 
  - Blue (#E3F2FD): Presentation Layer
  - Purple (#F3E5F5): Application Layer
  - Orange (#FFF3E0): Domain Layer
  - Green (#E8F5E9): Infrastructure Layer
  - Red (#FFEBEE): Infrastructure (databases, queues)
  - Light Green (#F1F8E9): Cross-Cutting Concerns
- **Activation Bars**: Show when component is active
- **Notes**: Provide additional context (headers, data structures)
- **Alt/Else**: Show conditional flows (feature flags, error handling)

## Key Differences from Go Version

| Aspect | Go (skeleton-api-go) | .NET (skeleton-api-net) |
|--------|---------------------|------------------------|
| **Layers** | Delivery → Endpoint → Use Case → Repository | Presentation → Application → Infrastructure |
| **gRPC Service** | Handler functions | Class inheriting UserGrpcServiceBase |
| **Validation** | go-playground/validator | FluentValidation |
| **ORM** | Raw SQL | Dapper (micro-ORM) |
| **Logging** | Uber Zap | Serilog |
| **APM** | Manual integration | Elastic.Apm.NetCoreAll (auto-instrumentation) |
| **DI** | Manual injection | Microsoft.Extensions.DependencyInjection |
| **Consumer** | Standalone consumer | BackgroundService (IHostedService) |
| **SSE** | HTTP handler | ASP.NET Controller |
| **Trace Context** | Manual extraction | Activity.Current (automatic) |

## Best Practices

1. **Keep diagrams up-to-date**: Update when architecture changes
2. **Use consistent naming**: Match actual code file/class names
3. **Document assumptions**: Use notes for important details
4. **Show error paths**: Include error handling flows
5. **Highlight async operations**: Mark best-effort operations
6. **Include trace context**: Show distributed tracing propagation
7. **Follow .NET conventions**: Use async/await, IDisposable, etc.

## References

- [PlantUML Sequence Diagram Documentation](https://plantuml.com/sequence-diagram)
- [W3C Trace Context Specification](https://www.w3.org/TR/trace-context/)
- [Clean Architecture by Robert C. Martin](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [.NET Clean Architecture Template](https://github.com/jasontaylordev/CleanArchitecture)
- [skeleton-api-net README](../README.md)
