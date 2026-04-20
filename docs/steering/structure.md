# Project Structure: Skeleton API .NET

## Data Flow
`Presentation (gRPC/REST) -> Application (Services) -> Domain (Entities/Interfaces) <- Infrastructure (Repositories)`

## Directory Layout
- `/src`: Main source code
  - `/SkeletonApi`: Presentation Layer (Web API, gRPC Services, Program.cs)
    - `/Protos`: gRPC definitions
    - `/Services`: gRPC/REST implementations
    - `/Extensions`: DI registrations
  - `/SkeletonApi.Application`: Application Layer
    - `/Interfaces`: Internal service interfaces
    - `/Services`: Business logic implementation
    - `/Models`: DTOs and Request/Response objects
    - `/Mappers`: Automapper or custom mapping logic
  - `/SkeletonApi.Domain`: Domain Layer
    - `/Entities`: Business entities
    - `/Repositories`: Repository interface definitions
    - `/Common`: Shared domain value objects
  - `/SkeletonApi.Infrastructure`: Infrastructure Layer
    - `/Repositories`: Database and Cache implementations (Dapper, Redis)
    - `/Messaging`: Message broker clients
    - `/ExternalServices`: Third-party API clients
  - `/SkeletonApi.Common`: Shared utilities and cross-cutting concerns
- `/tests`: Test projects (xUnit)
- `/tools`: Development tools (General CLI Generator)
- `/migrations`: SQL migration files
- `/docs`: Documentation and steering files
- `Makefile`: Build and automation tasks
- `Dockerfile`: Multi-stage Docker build
