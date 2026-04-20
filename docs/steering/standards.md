# Standards & Conventions: Skeleton API .NET

## Naming Conventions
- **Namespaces**: PascalCase (e.g., `SkeletonApi.Application.Services`).
- **Interfaces**: Start with 'I' (e.g., `IUserRepository`).
- **Classes/Structs**: PascalCase.
- **Methods**: PascalCase (Asynchronous methods should end in `Async`).
- **Parameters/Variables**: camelCase.
- **Constants**: PascalCase or SCREAMING_SNAKE_CASE.
- **Protobuf**:
  - Messages: PascalCase.
  - Fields: snake_case.
  - Services: PascalCase ending in `GrpcService`.

## Coding Standards
- **Asynchronous Programming**: 
  - Prefer `async`/`await` for all I/O operations.
  - Consistent use of `CancellationToken`.
- **Dependency Injection**: 
  - Use constructor injection.
  - Register services in `Extensions/ServiceCollectionExtensions.cs`.
- **Error Handling**: 
  - Use structured exceptions.
  - Map specific exceptions to HTTP status codes in the presentation layer.
- **Validation**:
  - Use FluentValidation for request model validation.

## Best Practices
- **Clean Code**: Follow SOLID principles and keep methods small.
- **Domain Driven Design (DDD) Lite**: Keep business logic in entities or application services.
- **Separation of Concerns**: Don't leak infrastructure details (SQL, Redis) into the application layer.
- **Logging**: Use Serilog for structured logging with correlation IDs.
- **Testing**:
  - Maintain high coverage for application services and domain logic.
  - Use Moq for mocking dependencies.
