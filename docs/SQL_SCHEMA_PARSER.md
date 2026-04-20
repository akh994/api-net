# SQL Schema Parser - Usage Guide

## Overview

The SQL schema parser allows you to generate complete CRUD API projects from SQL CREATE TABLE statements. It supports multiple tables in a single SQL file and generates a unified project with all entities.

## Quick Start

```bash
# Generate project from SQL schema
./tools/generator/bin/SkeletonApi.Generator project \
  --input examples/athlete_management.sql \
  --type schema \
  --name MyProject \
  --output ./MyProject
```

## SQL Schema Requirements

### ✅ Supported Features

- **Multiple Tables**: Parse multiple CREATE TABLE statements from one file
- **Data Types**: INT, BIGINT, VARCHAR, TEXT, DECIMAL, DATETIME, BOOLEAN, etc.
- **Primary Keys**: Both inline and constraint-based
- **Nullable Columns**: Detected from NOT NULL constraint
- **Comments**: COMMENT 'text' on columns

### ⚠️ Important Limitation: ID Types

**The generator currently only supports `VARCHAR`/`TEXT` primary keys (for GUID/UUID).**

**❌ This will cause build errors:**
```sql
CREATE TABLE `users` (
    `id` INT PRIMARY KEY,  -- ← INT not supported
    `name` VARCHAR(100)
);
```

**✅ Use this instead:**
```sql
CREATE TABLE `users` (
    `id` VARCHAR(36) PRIMARY KEY,  -- ← Use VARCHAR for GUID
    `name` VARCHAR(100)
);
```

### Recommended SQL Schema Pattern

```sql
CREATE TABLE `my_table` (
    `id` VARCHAR(36) PRIMARY KEY COMMENT 'Unique identifier (GUID)',
    `name` VARCHAR(255) NOT NULL COMMENT 'Display name',
    `description` TEXT COMMENT 'Optional description',
    `price` DECIMAL(10,2) NOT NULL COMMENT 'Price in currency',
    `is_active` BOOLEAN NOT NULL DEFAULT true COMMENT 'Active status',
    `created_at` DATETIME NOT NULL COMMENT 'Creation timestamp',
    `updated_at` DATETIME NOT NULL COMMENT 'Last update timestamp'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

## SQL Type Mapping

| SQL Type | C# Type | Proto Type |
|----------|---------|------------|
| INT, INTEGER | int | google.protobuf.Int32Value |
| BIGINT | long | google.protobuf.Int64Value |
| VARCHAR, TEXT | string | google.protobuf.StringValue |
| DECIMAL, NUMERIC | decimal | google.protobuf.DoubleValue |
| DATETIME, TIMESTAMP | DateTime | google.protobuf.Timestamp |
| BOOLEAN, BOOL | bool | google.protobuf.BoolValue |

## Generated Project Structure

```
MyProject/
├── src/
│   ├── MyProject/                    # Main API (gRPC + REST)
│   ├── MyProject.Application/        # Services, Validators, Interfaces
│   ├── MyProject.Domain/             # Entity classes
│   ├── MyProject.Infrastructure/     # Repositories (Dapper + MySQL)
│   ├── MyProject.Contracts/          # Proto files
│   └── MyProject.Common/             # Shared utilities
├── migrations/                       # SQL migrations
├── deployments/                      # Docker & Kubernetes
├── Makefile
└── README.md
```

## Generated CRUD Endpoints

For each table, the generator creates 5 endpoints:

- `POST /v1/{entity}` - Create
- `GET /v1/{entity}` - Get all
- `GET /v1/{entity}/{id}` - Get by ID
- `PUT /v1/{entity}/{id}` - Update
- `DELETE /v1/{entity}/{id}` - Delete

## Examples

### Single Table

```sql
CREATE TABLE `products` (
    `id` VARCHAR(36) PRIMARY KEY,
    `name` VARCHAR(255) NOT NULL,
    `price` DECIMAL(10,2) NOT NULL
);
```

```bash
./tools/generator/bin/SkeletonApi.Generator project \
  --input product.sql \
  --type schema \
  --name ProductService \
  --output ./ProductService
```

### Multiple Tables

See `examples/athlete_management.sql` for a complete example with 8 tables.

```bash
./tools/generator/bin/SkeletonApi.Generator project \
  --input examples/athlete_management.sql \
  --type schema \
  --name AthleteManagement \
  --output ./AthleteManagement
```

## Workaround for INT Primary Keys

If you must use INT primary keys in your existing database:

1. **Generate with VARCHAR first** (for clean generation)
2. **Manually update** the generated code:

```csharp
// In Service classes, remove this line:
entity.Id = Guid.NewGuid().ToString();  // ← Remove

// In Repository interfaces, change:
Task<Entity?> GetByIdAsync(string id);  // ← Change to int
Task DeleteAsync(string id);            // ← Change to int

// In Service interfaces, change:
Task<Entity?> GetByIdAsync(string id);  // ← Change to int
Task UpdateAsync(string id, Entity entity);  // ← Change to int
Task DeleteAsync(string id);            // ← Change to int
```

3. **Update Domain entity** if needed:
```csharp
public class MyEntity
{
    public int Id { get; set; } = 0;  // Already correct from SQL
    ...
}
```

## Future Enhancements

- [ ] Auto-detect ID type and generate appropriate code
- [ ] Support for foreign key relationships (navigation properties)
- [ ] Support for composite primary keys
- [ ] Support for database indexes
- [ ] Support for CHECK constraints
- [ ] Support for default values in code

## Troubleshooting

### Build Error: Cannot convert string to int

**Cause**: Your SQL uses `INT PRIMARY KEY` but generator expects `VARCHAR`.

**Solution**: Use `VARCHAR(36) PRIMARY KEY` in SQL schema, or manually fix generated code (see workaround above).

### No domain entities generated

**Cause**: Bug in earlier version (now fixed).

**Solution**: Rebuild generator with `make build-generator` and regenerate project.

## See Also

- [Generator Scenarios](../../../docs/GENERATOR_SCENARIOS.md)
- [Demo Walkthrough](DEMO_WALKTHROUGH.md)
