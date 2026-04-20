# Tech Stack: Skeleton API .NET

## Core
- **Framework**: .NET 10.0
- **Architecture**: Clean Architecture

## API & Communication
- **gRPC**: Grpc.AspNetCore
- **JSON Transcoding**: Microsoft.AspNetCore.Grpc.JsonTranscoding
- **Swagger/OpenAPI**: Swashbuckle.AspNetCore

## Database & Caching
- **ORM**: Dapper
- **Drivers**:
  - MySQL: MySql.Data
  - PostgreSQL: Npgsql
  - SQL Server: Microsoft.Data.SqlClient
- **Migrations**: github.com/golang-migrate/migrate/v4 (Managed via Makefile)
- **Cache**: StackExchange.Redis

## Messaging
- **RabbitMQ**: RabbitMQ.Client
- **Google Cloud Pub/Sub**: Google.Cloud.PubSub.V1
- **Apache Kafka**: Confluent.Kafka

## Observability & Resilience
- **Logging**: Serilog.AspNetCore
- **APM**: Elastic.Apm.NetCoreAll
- **Resilience**: Polly
- **Diagnostics**: Custom Diagnostic Server for dumps and metrics

## Feature Flags
- **Providers**: Flipt, Go Feature Flag
- **SDK**: OpenFeature.SDK

## Tools
- **CLI Generator**: Custom implementation in `tools/generator` (SkeletonApi.Generator)
- **Testing**: xUnit, Moq
- **Task Runner**: Makefile
