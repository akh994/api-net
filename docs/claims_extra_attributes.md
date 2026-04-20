# JWT Claims with ExtraAttributes

## Overview

The `skeleton-api-net` claims system supports capturing unmapped JWT claims through the `ExtraAttributes` dictionary in the `UserClaims` model. This allows you to include custom attributes in your JWT tokens without modifying the core `SkeletonApi.Common` code.

## Features

- ✅ Automatic capture of unmapped JWT claims
- ✅ Support for complex nested objects (deserialized as `Dictionary<string, object>` or `JsonElement`)
- ✅ Support for arrays
- ✅ Automatic propagation to downstream services (REST/gRPC)
- ✅ Standardized JSON serialization for header-based propagation

## Example: JWT with Complex Claims

### JWT Token Payload

```json
{
  "sub": "user123",
  "role": "admin",
  "department": {
    "id": "dept-001",
    "name": "Engineering",
    "location": "Jakarta"
  },
  "permissions": ["read", "write", "delete"],
  "metadata": {
    "teams": [
      {"id": "team1", "name": "Backend"},
      {"id": "team2", "name": "Frontend"}
    ],
    "preferences": {
      "theme": "dark",
      "language": "id"
    }
  }
}
```

### Resulting Claims in .NET

When the above JWT is parsed, the `UserClaims` object will contain:

```csharp
var claims = new UserClaims
{
    UserId = "user123",  // Mapped from "sub"
    Role = "admin",      // Mapped from "role"
    
    // All other claims are captured in ExtraAttributes
    ExtraAttributes = new Dictionary<string, object>
    {
        ["department"] = new Dictionary<string, object>
        {
            ["id"] = "dept-001",
            ["name"] = "Engineering",
            ["location"] = "Jakarta"
        },
        ["permissions"] = new List<object> { "read", "write", "delete" },
        ["metadata"] = new Dictionary<string, object>
        {
            ["teams"] = new List<object>
            {
                new Dictionary<string, object> { ["id"] = "team1", ["name"] = "Backend" },
                new Dictionary<string, object> { ["id"] = "team2", ["name"] = "Frontend" }
            },
            ["preferences"] = new Dictionary<string, object>
            {
                ["theme"] = "dark",
                ["language"] = "id"
            }
        }
    }
};
```

## Accessing ExtraAttributes

### Basic Access

```csharp
// Get claims from IUserContext
var userClaims = _userContext.GetUser();

// Access standard claims (strongly typed)
string userId = userClaims.UserId; // "user123"
string role = userClaims.Role;     // "admin"

// Access extra attributes (dictionary access with type casting)
if (userClaims.ExtraAttributes.TryGetValue("department", out var deptObj) && 
    deptObj is Dictionary<string, object> dept)
{
    string deptId = dept["id"].ToString();         // "dept-001"
    string deptName = dept["name"].ToString();     // "Engineering"
    string location = dept["location"].ToString(); // "Jakarta"
}
```

### Accessing Arrays

```csharp
// Access array of strings
if (userClaims.ExtraAttributes.TryGetValue("permissions", out var permsObj) && 
    permsObj is IEnumerable<object> perms)
{
    foreach (var perm in perms)
    {
        Console.WriteLine(perm.ToString()); // "read", "write", "delete"
    }
}
```

### Accessing Nested Objects

```csharp
// Access deeply nested structures
if (userClaims.ExtraAttributes.TryGetValue("metadata", out var metadataObj) && 
    metadataObj is Dictionary<string, object> metadata)
{
    // Access nested array of objects
    if (metadata.TryGetValue("teams", out var teamsObj) && 
        teamsObj is IEnumerable<object> teams)
    {
        foreach (var team in teams)
        {
            if (team is Dictionary<string, object> t)
            {
                string teamId = t["id"].ToString();
                string teamName = t["name"].ToString();
                Console.WriteLine($"Team: {teamName} ({teamId})");
            }
        }
    }
    
    // Access nested object
    if (metadata.TryGetValue("preferences", out var prefsObj) && 
        prefsObj is Dictionary<string, object> prefs)
    {
        string theme = prefs["theme"].ToString();       // "dark"
        string language = prefs["language"].ToString(); // "id"
    }
}
```

## Propagation to Downstream Services

`ExtraAttributes` are automatically propagated when calling downstream services using the provided interceptors and handlers.

### gRPC Client Propagation

The `ClaimsPropagationInterceptor` automatically serializes `ExtraAttributes` into the `x-extra-attributes` metadata header.

```csharp
// Register in DependencyInjection.cs
services.AddGrpcClient<MyService.MyServiceClient>(o => ...)
        .AddInterceptor<ClaimsPropagationInterceptor>();
```

### REST Client Propagation

The `ClaimsPropagationHandler` (DelegatingHandler) handles propagation for `HttpClient`.

```csharp
// Register in DependencyInjection.cs
services.AddHttpClient<IMyRestClient, MyRestClient>(o => ...)
        .AddHttpMessageHandler<ClaimsPropagationHandler>();
```

### Downstream Service Extraction

In the downstream service, the `ClaimsMiddleware` (for HTTP/REST) or `BaseMessageConsumer` (for MQ) automatically deserializes the `x-extra-attributes` header back into the `ExtraAttributes` dictionary.

## Important Notes

### Type Considerations

- **Numbers**: When deserialized from headers, numbers might be `JsonElement` or `double` depending on the serializer settings. `ToString()` is usually the safest way to extract the value if the exact type isn't critical.
- **Nested Objects**: Always check types using `is Dictionary<string, object>` or `is IEnumerable<object>`.

### Excluded Claims

The following claims are **NOT** included in `ExtraAttributes` as they are either standard JWT claims or already mapped to strongly-typed properties in `UserClaims`:

**Standard JWT claims:**
- `iss`, `aud`, `exp`, `nbf`, `iat`, `jti`
- `sub` (mapped to `UserId`)

**Custom claims already mapped:**
- `role` / `x-user-role` (mapped to `Role`)
- `user_uuid` / `x-user-uuid` (mapped to `UserUuid`)
- `email` / `x-user-email` (mapped to `Email`)
- `domain_id` / `x-domain-id` (mapped to `DomainId`)
- `domain_name` / `x-domain-name` (mapped to `DomainName`)
- `group_name` / `x-group-name` (mapped to `GroupName`)

## Best Practices

1. **Use safe type checks** (`is` and `TryGetValue`) to prevent `KeyNotFoundException` or `InvalidCastException`.
2. **Document custom claims** used by your services so other teams know what to expect.
3. **Consistency**: Use the same claim names across Go and .NET services.
4. **Avoid deep nesting** in JWTs to keep processing overhead low.

## See Also

- [UserClaims.cs](../../src/SkeletonApi.Common/Models/UserClaims.cs)
- [ClaimsMiddleware.cs](../../src/SkeletonApi.Common/Middleware/ClaimsMiddleware.cs)
- [ClaimsPropagationInterceptor.cs](../../src/SkeletonApi.Common/GrpcClient/ClaimsPropagationInterceptor.cs)
