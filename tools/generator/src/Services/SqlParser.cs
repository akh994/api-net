using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SkeletonApi.Generator.Models;

namespace SkeletonApi.Generator.Services;

public class SqlParser
{
    /// <summary>
    /// Parse multiple CREATE TABLE statements from SQL schema
    /// </summary>
    public List<EntityDefinition> ParseMultiple(string sqlContent)
    {
        var entities = new List<EntityDefinition>();

        // Extract all CREATE TABLE statements
        var createTablePattern = @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?`?(\w+)`?\s*\((.*?)\)(?:\s*ENGINE.*?)?;";
        var matches = Regex.Matches(sqlContent, createTablePattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var tableName = match.Groups[1].Value;
            var tableBody = match.Groups[2].Value;

            var entity = ParseTable(tableName, tableBody);
            entities.Add(entity);
        }

        return entities;
    }

    /// <summary>
    /// Parse single CREATE TABLE statement
    /// </summary>
    public EntityDefinition Parse(string sqlContent)
    {
        var entities = ParseMultiple(sqlContent);

        if (entities.Count == 0)
            throw new InvalidOperationException("No CREATE TABLE statement found in SQL");

        if (entities.Count > 1)
            throw new InvalidOperationException($"Multiple tables found ({entities.Count}). Use ParseMultiple() instead.");

        return entities[0];
    }

    private EntityDefinition ParseTable(string tableName, string tableBody)
    {
        var entityName = ToPascalCase(tableName);
        var fields = new List<FieldDefinition>();

        // Split by comma, but be careful with commas inside COMMENT
        var lines = SplitTableBody(tableBody);

        string? primaryKeyColumn = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip PRIMARY KEY constraint lines
            if (trimmed.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
            {
                // Extract column name from PRIMARY KEY (`id`)
                var pkMatch = Regex.Match(trimmed, @"PRIMARY\s+KEY\s*\(`?(\w+)`?\)", RegexOptions.IgnoreCase);
                if (pkMatch.Success)
                {
                    primaryKeyColumn = pkMatch.Groups[1].Value;
                }
                continue;
            }

            // Skip FOREIGN KEY, KEY, INDEX, UNIQUE, etc.
            if (Regex.IsMatch(trimmed, @"^(FOREIGN\s+KEY|KEY|INDEX|UNIQUE|CONSTRAINT)", RegexOptions.IgnoreCase))
                continue;

            // Parse column definition
            var field = ParseColumn(trimmed);
            if (field != null)
            {
                fields.Add(field);
            }
        }

        // Mark primary key field
        if (primaryKeyColumn != null)
        {
            var pkField = fields.Find(f => f.Name.Equals(primaryKeyColumn, StringComparison.OrdinalIgnoreCase));
            if (pkField != null)
            {
                pkField.PrimaryKey = true;
                pkField.Nullable = false; // Primary keys should always be non-nullable
                if (pkField.Type.EndsWith("?"))
                    pkField.Type = pkField.Type.TrimEnd('?');
            }
        }

        // Generate standard CRUD methods
        var methods = GenerateStandardMethods(entityName);

        // Generate proto content
        var protoContent = GenerateProtoContent(entityName, tableName, fields, methods);

        // Create main entity message for domain generation
        var mainMessage = new MessageDefinition
        {
            Name = entityName,
            Fields = fields
        };

        return new EntityDefinition
        {
            Name = entityName,
            ServiceName = $"{entityName}GrpcService",
            TableName = tableName,
            ProtoMessageName = entityName,
            Fields = fields,
            Methods = methods,
            Messages = new List<MessageDefinition> { mainMessage },
            ProtoContent = protoContent
        };
    }

    private string GetIdProtoType(List<FieldDefinition> fields)
    {
        var idField = fields.Find(f => f.Name.Equals("Id", StringComparison.OrdinalIgnoreCase));
        if (idField != null)
        {
            return idField.Type switch
            {
                "int" => "int32",
                "long" => "int64",
                "Guid" => "string",
                _ => "string"
            };
        }
        return "string";
    }

    private List<string> SplitTableBody(string tableBody)
    {
        var lines = new List<string>();
        var currentLine = new System.Text.StringBuilder();
        var inComment = false;
        var parenDepth = 0;

        for (int i = 0; i < tableBody.Length; i++)
        {
            var c = tableBody[i];

            if (c == '(') parenDepth++;
            if (c == ')') parenDepth--;

            // Track COMMENT '...'
            if (i < tableBody.Length - 7 && tableBody.Substring(i, 7).Equals("COMMENT", StringComparison.OrdinalIgnoreCase))
            {
                inComment = true;
            }

            if (inComment && c == '\'')
            {
                // Check if it's closing quote
                var quoteCount = 0;
                for (int j = i; j < tableBody.Length && tableBody[j] == '\''; j++)
                {
                    quoteCount++;
                }
                if (quoteCount == 1 || (quoteCount % 2 == 0))
                {
                    inComment = false;
                }
            }

            if (c == ',' && parenDepth == 0 && !inComment)
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
            }
            else
            {
                currentLine.Append(c);
            }
        }

        var lastLine = currentLine.ToString();
        if (!string.IsNullOrWhiteSpace(lastLine))
        {
            lines.Add(lastLine);
        }

        return lines;
    }

    private FieldDefinition? ParseColumn(string columnDef)
    {
        // Pattern: `column_name` TYPE [constraints] [COMMENT 'text']
        var match = Regex.Match(columnDef, @"`?(\w+)`?\s+(\w+)(?:\(([^)]+)\))?(.*)", RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        var columnName = match.Groups[1].Value;
        var sqlType = match.Groups[2].Value.ToUpper();
        var typeParams = match.Groups[3].Value; // e.g., "10,2" for DECIMAL(10,2)
        var constraints = match.Groups[4].Value;

        // Check for PRIMARY KEY inline
        var isPrimaryKey = Regex.IsMatch(constraints, @"\bPRIMARY\s+KEY\b", RegexOptions.IgnoreCase);

        // Check for NOT NULL
        var isNullable = !Regex.IsMatch(constraints, @"\bNOT\s+NULL\b", RegexOptions.IgnoreCase) && !isPrimaryKey;

        // Extract COMMENT
        string? comment = null;
        var commentMatch = Regex.Match(constraints, @"COMMENT\s+'([^']*)'", RegexOptions.IgnoreCase);
        if (commentMatch.Success)
        {
            comment = commentMatch.Groups[1].Value;
        }

        // Map SQL type to C# type
        var csharpType = MapSqlTypeToCSharp(sqlType, typeParams);

        if (isNullable && csharpType != "string" && csharpType != "byte[]" && !csharpType.EndsWith("?"))
        {
            csharpType += "?";
        }

        return new FieldDefinition
        {
            Name = ToPascalCase(columnName),
            Type = csharpType,
            Nullable = isNullable,
            PrimaryKey = isPrimaryKey,
            Comment = comment
        };
    }

    private string MapSqlTypeToCSharp(string sqlType, string typeParams)
    {
        return sqlType switch
        {
            "INT" or "INTEGER" or "MEDIUMINT" => "int",
            "BIGINT" => "long",
            "SMALLINT" or "TINYINT" => "short",
            "VARCHAR" or "CHAR" or "TEXT" or "LONGTEXT" or "MEDIUMTEXT" or "TINYTEXT" => "string",
            "DECIMAL" or "NUMERIC" or "MONEY" => "decimal",
            "FLOAT" => "float",
            "DOUBLE" or "REAL" => "double",
            "DATETIME" or "TIMESTAMP" or "DATE" or "TIME" => "DateTime",
            "BOOLEAN" or "BOOL" or "BIT" => "bool",
            "GUID" or "UUID" => "Guid",
            "BLOB" or "BINARY" or "VARBINARY" => "byte[]",
            _ => "string" // Default fallback
        };
    }

    private List<MethodDefinition> GenerateStandardMethods(string entityName)
    {
        return new List<MethodDefinition>
        {
            new MethodDefinition
            {
                Name = "Add",
                RequestType = entityName,
                ResponseType = $"Res{entityName}Message",
                HttpMethod = "POST",
                HttpPath = $"/v1/{entityName.ToLower()}"
            },
            new MethodDefinition
            {
                Name = "GetAll",
                RequestType = $"{entityName}Empty",
                ResponseType = $"Res{entityName}All",
                HttpMethod = "GET",
                HttpPath = $"/v1/{entityName.ToLower()}"
            },
            new MethodDefinition
            {
                Name = "GetById",
                RequestType = $"{entityName}ByIdRequest",
                ResponseType = entityName,
                HttpMethod = "GET",
                HttpPath = $"/v1/{entityName.ToLower()}/{{id}}"
            },
            new MethodDefinition
            {
                Name = "Update",
                RequestType = entityName,
                ResponseType = $"Res{entityName}Message",
                HttpMethod = "PUT",
                HttpPath = $"/v1/{entityName.ToLower()}/{{id}}"
            },
            new MethodDefinition
            {
                Name = "Delete",
                RequestType = $"{entityName}ByIdRequest",
                ResponseType = $"Res{entityName}Message",
                HttpMethod = "DELETE",
                HttpPath = $"/v1/{entityName.ToLower()}/{{id}}"
            },
            new MethodDefinition
            {
                Name = "GetAllPaginated",
                RequestType = $"{entityName}PaginationRequest",
                ResponseType = $"Res{entityName}Paginated",
                HttpMethod = "GET",
                HttpPath = $"/v1/{entityName.ToLower()}s/paginated"
            }
        };
    }

    private string GenerateProtoContent(string entityName, string tableName, List<FieldDefinition> fields, List<MethodDefinition> methods)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($@"syntax = ""proto3"";

package proto;

option csharp_namespace = ""SkeletonApi.Contracts"";

");

        var hasDateTime = fields.Any(f => f.Type.TrimEnd('?').Equals("DateTime", StringComparison.OrdinalIgnoreCase));
        var hasNullable = fields.Any(f => f.Nullable && new[] { "int", "long", "short", "decimal", "float", "double", "bool" }.Contains(f.Type.TrimEnd('?')));

        if (hasNullable)
        {
            sb.AppendLine("import \"google/protobuf/wrappers.proto\";");
        }
        if (hasDateTime)
        {
            sb.AppendLine("import \"google/protobuf/timestamp.proto\";");
        }
        sb.AppendLine("import \"google/api/annotations.proto\";");

        sb.Append($@"
// {entityName} message
message {entityName} {{
");

        int fieldNumber = 1;
        foreach (var field in fields)
        {
            var protoType = MapCSharpTypeToProto(field.Type, field.Nullable);
            var jsonName = ToSnakeCase(field.Name);
            sb.AppendLine($"    {protoType} {ToSnakeCase(field.Name)} = {fieldNumber} [json_name=\"{jsonName}\"];");
            fieldNumber++;
        }

        sb.Append($@"}}

// Response messages
message Res{entityName}All {{
    repeated {entityName} items = 1;
}}

message {entityName}Empty {{
}}

message Res{entityName}Message {{
    string message = 1;
}}

message {entityName}ByIdRequest {{
    {GetIdProtoType(fields)} id = 1 [json_name=""id""];
}}

// Pagination request
message {entityName}PaginationRequest {{
    int32 page = 1 [json_name=""page""];
    int32 page_size = 2 [json_name=""page_size""];
}}

// Pagination metadata
message {entityName}PaginationMeta {{
    int32 page = 1 [json_name=""page""];
    int32 page_size = 2 [json_name=""page_size""];
    int64 total_items = 3 [json_name=""total_items""];
    int32 total_pages = 4 [json_name=""total_pages""];
}}

// Paginated response
message Res{entityName}Paginated {{
    repeated {entityName} items = 1 [json_name=""items""];
    {entityName}PaginationMeta pagination = 2 [json_name=""pagination""];
}}

// gRPC Service
service {entityName}GrpcService {{
");

        foreach (var method in methods)
        {
            var httpMethod = method.HttpMethod?.ToLower() ?? "post";
            var httpPath = method.HttpPath ?? $"/v1/{entityName.ToLower()}";
            var hasBody = httpMethod == "post" || httpMethod == "put" || httpMethod == "patch";

            sb.Append($@"    rpc {method.Name}({method.RequestType}) returns ({method.ResponseType}) {{
        option(google.api.http) = {{
            {httpMethod}: ""{httpPath}""");

            if (hasBody)
            {
                sb.Append(@",
            body:""*""");
            }

            sb.Append(@"
        };
    }

");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private string MapCSharpTypeToProto(string csharpType, bool nullable)
    {
        // Strip nullable ? indicator from csharpType if present for easier matching
        var baseCSharpType = csharpType.TrimEnd('?');

        // If it's explicitly nullable or reference type we might want wrappers
        // But for consistency with SmartProjectGenerator's Mapper, we prefer primitives where possible

        return baseCSharpType switch
        {
            "int" => nullable ? "google.protobuf.Int32Value" : "int32",
            "long" => nullable ? "google.protobuf.Int64Value" : "int64",
            "short" => nullable ? "google.protobuf.Int32Value" : "int32",
            "string" => "string", // proto3 string is optional by default
            "decimal" => nullable ? "google.protobuf.DoubleValue" : "double",
            "float" => nullable ? "google.protobuf.FloatValue" : "float",
            "double" => nullable ? "google.protobuf.DoubleValue" : "double",
            "bool" => nullable ? "google.protobuf.BoolValue" : "bool",
            "DateTime" => "google.protobuf.Timestamp",
            "Guid" => "string",
            "byte[]" => "bytes",
            _ => "string"
        };
    }

    private string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Split by underscore
        var parts = input.Split('_');
        var sb = new System.Text.StringBuilder();

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            sb.Append(char.ToUpper(part[0]));
            sb.Append(part.Substring(1).ToLower());
        }

        return sb.ToString();
    }

    private string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Insert underscore before uppercase letters
        var result = Regex.Replace(input, "([a-z])([A-Z])", "$1_$2");
        return result.ToLower();
    }
}
