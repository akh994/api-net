# Multi-stage Dockerfile for skeleton-api-net

# Stage 1: Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

# Install build dependencies
# RUN apt-get update && apt-get install -y \
#     protobuf-compiler \
#     && rm -rf /var/lib/apt/lists/*

# Set working directory
WORKDIR /app

# Copy solution and project files
COPY *.sln ./
COPY src/SkeletonApi/*.csproj ./src/SkeletonApi/
COPY src/SkeletonApi.Application/*.csproj ./src/SkeletonApi.Application/
COPY src/SkeletonApi.Domain/*.csproj ./src/SkeletonApi.Domain/
COPY src/SkeletonApi.Infrastructure/*.csproj ./src/SkeletonApi.Infrastructure/
COPY src/SkeletonApi.Common/*.csproj ./src/SkeletonApi.Common/
COPY src/SkeletonApi.Contracts/*.csproj ./src/SkeletonApi.Contracts/
COPY tests/SkeletonApi.Tests/*.csproj ./tests/SkeletonApi.Tests/
COPY tools/generator/src/*.csproj ./tools/generator/src/
COPY tools/HealthCheck/*.csproj ./tools/HealthCheck/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY . .

# Build and publish the application
RUN dotnet publish src/SkeletonApi/SkeletonApi.csproj -c Release -o /app/publish

# Build and publish the healthcheck tool (self-contained, single file)
RUN dotnet publish tools/HealthCheck/HealthCheck.csproj -c Release -o /app/healthcheck

# Stage 2: Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Set timezone
ENV TZ=UTC

# Set working directory
WORKDIR /app

# Copy published output from builder
COPY --from=builder /app/publish .

# Copy healthcheck tool
COPY --from=builder /app/healthcheck/HealthCheck ./healthcheck

# Copy credentials folder
#COPY --from=builder /app/credentials ./credentials

# Copy proto files for Swagger
COPY --from=builder /app/src/SkeletonApi.Contracts/Protos ./proto

# Expose ports
EXPOSE 4021 4022 4023

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD ["./healthcheck"]

# Run the application
ENTRYPOINT ["dotnet", "SkeletonApi.dll"]
