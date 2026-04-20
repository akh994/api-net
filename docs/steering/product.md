# Product: Skeleton API .NET

## Purpose
**skeleton-api-net** is a project template for building microservices based on .NET 10 with a scalable, maintainable, and production-ready architecture. It provides a solid foundation with various enterprise-grade features ready for use.

## Key Features
- **Architecture**: Clean Architecture (Domain, Application, Infrastructure, Presentation).
- **Communication**: Dual Protocol Support (gRPC + JSON Transcoding for REST), HTTPS/TLS.
- **Database**: Multi-Database Support (MySQL, PostgreSQL, SQL Server), Dapper ORM, Redis Caching.
- **Messaging**: Multi-Provider Support (RabbitMQ, Google Cloud Pub/Sub, Apache Kafka).
- **Resilience**: Circuit Breaker (Polly), Retry with Exponential Backoff, Timeout Management, Graceful Shutdown (180s).
- **Observability**: Structured Logging (Serilog), Elastic APM (Tracing), Health Checks (/health/live, /health/ready), Diagnostic Server.
- **Feature Flags**: Multi-provider support (Flipt, Go Feature Flag) with IMemoryCache.
- **DevOps**: Docker (Non-root user), Kubernetes (HPA, Zero-downtime), CI/CD (Jenkins, SonarQube).
- **Automation**: Code Generator CLI for scaffolding entities, clients, MQ, and cache with incremental updates.
