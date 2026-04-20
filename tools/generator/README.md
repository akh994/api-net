# Skeleton API .NET - Code Generator

Generator otomatis untuk membuat project atau entity baru berdasarkan `skeleton-api-net` dari proto file, JSON model, atau database schema.

## 📦 Installation

```bash
cd tools/generator
dotnet build src/SkeletonApi.Generator.csproj -o bin
```

Binary akan dihasilkan di `tools/generator/bin/SkeletonApi.Generator`.

## 🎯 Fitur

| Command | Deskripsi |
|---------|-----------|
| `project` | Generate project baru (Full API, MQ-only, atau Entity-based) |
| `entity` | Tambah entity baru ke project yang sudah ada |
| `client` | Generate external service client (REST/gRPC) |
| `mq` | Generate message publisher (RabbitMQ/PubSub) |
| `consumer` | Generate message consumer logic |

### Global Flags

| Flag | Deskripsi |
|------|-----------|
| `--update` | Update inkremental (wire method baru ke semua layer) |
| `--only-domain-mapper` | Update hanya Domain Entities dan API Mappers |
| `--only-domain-contracts` | Update hanya Domain Entities dan Proto Contracts |

## 🚀 Usage

### Generate Project Baru (dari Proto)

```bash
./tools/generator/bin/SkeletonApi.Generator project \
  --input user.proto \
  --type proto \
  --name my-service \
  --output ./my-service
```

### Generate MQ-Only Project (Consumer-Only)

```bash
./tools/generator/bin/SkeletonApi.Generator project \
  --input mq_subscriber.json \
  --type mq \
  --name worker-service \
  --output ./worker-service
```

### Tambah Entity ke Project

```bash
./tools/generator/bin/SkeletonApi.Generator entity \
  --input product.proto \
  --type proto \
  --output .
```

### Generate External Client

```bash
./tools/generator/bin/SkeletonApi.Generator client \
  --input payment.proto \
  --type grpc \
  --name PaymentService \
  --output .
```

### Generate MQ Publisher

```bash
./tools/generator/bin/SkeletonApi.Generator mq \
  --input mq_publisher.json \
  --output .
```

### Generate Consumer

```bash
./tools/generator/bin/SkeletonApi.Generator consumer \
  --input mq_subscriber.json \
  --output .
```


## 📝 Input Format Examples

### JSON Model Format

```json
{
  "name": "Product",
  "table_name": "products",
  "fields": [
    {"name": "Id", "type": "Guid", "primary_key": true},
    {"name": "Name", "type": "string", "nullable": false},
    {"name": "Price", "type": "decimal", "nullable": false}
  ],
  "methods": ["Add", "GetAll", "GetByID", "Update", "Delete"]
}
```

### Proto File Format

```protobuf
syntax = "proto3";
package user;

message User {
  string id = 1;
  string username = 2;
  string email = 3;
}

service UserGrpcService {
  rpc Add(User) returns (UserResponse);
  rpc GetAll(Empty) returns (UserListResponse);
  rpc GetById(UserByIdRequest) returns (User);
}
```

## 🛠️ Development Status

| Command | Status |
|---------|--------|
| `project` | ✅ Stable (Supports Incremental & Selective) |
| `entity` | ✅ Stable |
| `client` | ✅ Basic Implementation |
| `mq` | ✅ Basic Implementation |
| `consumer` | ✅ Basic Implementation |

## 📄 License

Same as skeleton-api-net project license.
