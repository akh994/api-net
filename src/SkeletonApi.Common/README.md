# SkeletonApi.Common

Common utilities library untuk skeleton-api-net. Library ini berisi reusable components yang bisa di-share ke multiple microservices.

## 📦 Components

### HTTP Client (`Http/`)
- **ResilientHttpClient** - HTTP client dengan retry dan circuit breaker
  - Automatic retry dengan exponential backoff
  - Circuit breaker untuk fault tolerance
  - Generic methods untuk JSON serialization

### Error Handling (`Errors/`)
- **CustomExceptions** - Standard exception types
  - `NotFoundException`
  - `ValidationException`
  - `ConflictException`
  - `UnauthorizedException`
  - `ForbiddenException`
  - `BadRequestException`

### Extensions (`Extensions/`)
- **StringExtensions** - String manipulation utilities
  - `ToCamelCase()`, `ToPascalCase()`, `ToSnakeCase()`
  - `Truncate()`, `NullIfEmpty()`
  
- **DateTimeExtensions** - DateTime utilities
  - `ToUnixTimeSeconds()`, `FromUnixTimeSeconds()`
  - `StartOfDay()`, `EndOfDay()`
  - `StartOfMonth()`, `EndOfMonth()`

### Utilities (`Utilities/`)
- **IdGenerator** - ID generation utilities
  - `NewId()` - GUID
  - `NewShortId()` - GUID without hyphens
  - `NewSortableId()` - Timestamp + GUID
  - `NewRandomString()` - Random alphanumeric

### Logging (`Logging/`)
- **LoggingExtensions** - Structured logging helpers
  - `BeginScope()` with properties
  - Log methods with structured properties

### Caching (`Caching/`)
- **CacheKeyBuilder** - Consistent cache key generation
  - `BuildForEntity<T>()`
  - `BuildForList<T>()`
  - `BuildForSearch<T>()`
  - `BuildForPaginated<T>()`
  
- **CacheExpiration** - Standard expiration times
  - `Short` (5 min)
  - `Medium` (15 min)
  - `Long` (1 hour)
  - `VeryLong` (24 hours)

### Resilience (`Resilience/`)
- **ResiliencePolicies** - Pre-configured Polly policies
  - `CreateRetryPolicy<T>()`
  - `CreateCircuitBreakerPolicy<T>()`
  - `CreateCombinedPolicy<T>()`
  - `CreateTimeoutPolicy<T>()`

## 🚀 Usage Examples

### HTTP Client with Resilience
```csharp
var httpClient = new HttpClient();
var logger = serviceProvider.GetRequiredService<ILogger<ResilientHttpClient>>();

var resilientClient = new ResilientHttpClient(httpClient, logger, new HttpClientOptions
{
    MaxRetries = 3,
    RetryDelayMs = 100,
    CircuitBreakerFailureThreshold = 0.5
});

// GET request
var user = await resilientClient.GetAsync<User>("https://api.example.com/users/1");

// POST request
var newUser = await resilientClient.PostAsync<User>("https://api.example.com/users", new { name = "John" });
```

### Cache Key Generation
```csharp
using SkeletonApi.Common.Caching;

// Entity cache key
var key = CacheKeyBuilder.BuildForEntity<User>("user-123");
// Result: "user:user-123"

// List cache key
var listKey = CacheKeyBuilder.BuildForList<User>("active");
// Result: "user:list:active"

// Paginated cache key
var pageKey = CacheKeyBuilder.BuildForPaginated<User>(2, 20);
// Result: "user:paginated:p2:s20"

// Use with cache
await cacheRepository.SetAsync(key, user, CacheExpiration.Medium);
```

### String Extensions
```csharp
using SkeletonApi.Common.Extensions;

var text = "HelloWorld";
text.ToCamelCase();    // "helloWorld"
text.ToSnakeCase();    // "hello_world"
text.Truncate(5);      // "Hello"
```

### DateTime Extensions
```csharp
using SkeletonApi.Common.Extensions;

var now = DateTime.Now;
now.StartOfDay();      // Today at 00:00:00
now.EndOfDay();        // Today at 23:59:59.999
now.ToUnixTimeSeconds(); // Unix timestamp
```

### ID Generation
```csharp
using SkeletonApi.Common.Utilities;

var id = IdGenerator.NewId();              // "550e8400-e29b-41d4-a716-446655440000"
var shortId = IdGenerator.NewShortId();    // "550e8400e29b41d4a716446655440000"
var sortableId = IdGenerator.NewSortableId(); // "0000018c1234abcd1234567890abcdef"
var randomStr = IdGenerator.NewRandomString(8); // "aB3dE9fG"
```

### Resilience Policies
```csharp
using SkeletonApi.Common.Resilience;

// Retry policy
var retryPolicy = ResiliencePolicies.CreateRetryPolicy<HttpResponseMessage>(
    maxRetries: 3,
    initialDelayMs: 100
);

// Circuit breaker
var circuitBreaker = ResiliencePolicies.CreateCircuitBreakerPolicy<HttpResponseMessage>(
    failureThreshold: 0.5,
    minimumThroughput: 10,
    durationSeconds: 30
);

// Combined policy
var combined = ResiliencePolicies.CreateCombinedPolicy<HttpResponseMessage>();

// Execute with policy
var result = await combined.ExecuteAsync(async ct => 
    await httpClient.GetAsync("https://api.example.com", ct), 
    cancellationToken);
```

### Structured Logging
```csharp
using SkeletonApi.Common.Logging;

logger.LogInformation(
    "User created successfully",
    ("UserId", userId),
    ("Username", username),
    ("Email", email)
);
```

### Custom Exceptions
```csharp
using SkeletonApi.Common.Errors;

// Not found
throw new NotFoundException($"User with ID {id} not found");

// Validation error
throw new ValidationException(new Dictionary<string, string[]>
{
    ["Email"] = new[] { "Email is required", "Email format is invalid" },
    ["Password"] = new[] { "Password must be at least 6 characters" }
});

// Conflict
throw new ConflictException($"User with email {email} already exists");
```

## 📦 Dependencies

- `Microsoft.Extensions.Logging.Abstractions` - Logging abstractions
- `Microsoft.Extensions.Http` - HTTP client factory
- `Polly` - Resilience and transient fault handling
- `System.Net.Http.Json` - JSON serialization for HTTP

## 🔧 Installation

Add reference to your project:

```bash
dotnet add reference ../SkeletonApi.Common/SkeletonApi.Common.csproj
```

Or add to `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\SkeletonApi.Common\SkeletonApi.Common.csproj" />
</ItemGroup>
```

## 📝 Best Practices

1. **Use ResilientHttpClient** untuk semua external HTTP calls
2. **Use CacheKeyBuilder** untuk consistent cache keys
3. **Use standard CacheExpiration** times
4. **Use custom exceptions** untuk domain-specific errors
5. **Use extension methods** untuk cleaner code
6. **Use ResiliencePolicies** untuk fault tolerance

## 🚀 Future Enhancements

- [ ] Message broker abstractions (RabbitMQ, Kafka, Pub/Sub)
- [ ] Health check utilities
- [ ] Metrics and telemetry helpers
- [ ] Configuration validators
- [ ] Security utilities (encryption, hashing)
