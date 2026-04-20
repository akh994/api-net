using System.Text;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using SkeletonApi.Generator.Models;

namespace SkeletonApi.Generator.Services;

public class OpenApiParser
{
    public EntityDefinition Parse(string content)
    {
        var reader = new OpenApiStringReader(new OpenApiReaderSettings
        {
            RuleSet = Microsoft.OpenApi.Validations.ValidationRuleSet.GetDefaultRuleSet()
        });

        var document = reader.Read(content, out var diagnostic);

        // Only fail on critical errors, not warnings
        var criticalErrors = diagnostic.Errors.Where(e =>
            !e.Message.Contains("Responses must contain") &&
            !e.Message.Contains("must be a valid runtime expression")).ToList();

        if (criticalErrors.Count > 0)
        {
            var errors = string.Join(", ", criticalErrors.Select(e => e.Message));
            throw new InvalidOperationException($"Failed to parse OpenAPI document: {errors}");
        }

        // Determine entity name from title
        var entityName = GetEntityName(document);
        var serviceName = entityName + "Service";
        var tableName = ToSnakeCase(entityName) + "s";

        var entity = new EntityDefinition
        {
            Name = entityName,
            ServiceName = serviceName,
            TableName = tableName,
            ProtoMessageName = entityName + "Model",
            ProtoContent = "", // Will be set later
            Fields = new List<FieldDefinition>(),
            Methods = new List<MethodDefinition>(),
            Messages = new List<MessageDefinition>()
        };

        // Extract schemas
        if (document.Components?.Schemas != null)
        {
            foreach (var schema in document.Components.Schemas.OrderBy(s => s.Key))
            {
                var message = new MessageDefinition
                {
                    Name = ToPascalCase(schema.Key),
                    Fields = ExtractFields(schema.Value)
                };
                entity.Messages.Add(message);

                // Use first schema as main entity
                if (entity.Fields.Count == 0)
                {
                    entity.Fields = message.Fields;
                }
            }
        }

        // Ensure main entity is in Messages
        if (!entity.Messages.Any(m => m.Name == entity.Name))
        {
            entity.Messages.Add(new MessageDefinition
            {
                Name = entity.Name,
                Fields = entity.Fields
            });
        }

        // Ensure entity has at least an ID field
        if (entity.Fields.Count == 0)
        {
            entity.Fields.Add(new FieldDefinition
            {
                Name = "Id",
                Type = "string",
                Nullable = false,
                PrimaryKey = true,
                Comment = "Primary key"
            });
        }

        // Extract methods from paths
        if (document.Paths != null)
        {
            foreach (var path in document.Paths.OrderBy(p => p.Key))
            {
                ExtractMethodsFromPath(path.Key, path.Value, entity);
            }
        }

        // Generate proto content
        entity.ProtoContent = GenerateProtoContent(entity);

        return entity;
    }

    private string GetEntityName(OpenApiDocument document)
    {
        var title = document.Info?.Title ?? "Service";
        // Remove common suffixes like "API", "Service"
        title = title.Replace(" API", "").Replace(" Service", "").Trim();
        return ToPascalCase(title);
    }

    private List<FieldDefinition> ExtractFields(OpenApiSchema schema)
    {
        var fields = new List<FieldDefinition>();

        if (schema.Properties == null)
            return fields;

        foreach (var prop in schema.Properties.OrderBy(p => p.Key))
        {
            var fieldName = ToPascalCase(prop.Key);
            var fieldType = MapOpenApiTypeToCSharp(prop.Value);
            var isRequired = schema.Required?.Contains(prop.Key) ?? false;
            var isNullable = !isRequired;

            // Append ? to value types if nullable
            if (isNullable && IsValueType(fieldType))
            {
                fieldType += "?";
            }

            fields.Add(new FieldDefinition
            {
                Name = fieldName,
                Type = fieldType,
                Nullable = isNullable,
                IsRepeated = prop.Value.Type == "array",
                PrimaryKey = prop.Key.ToLower() == "id" || prop.Key.EndsWith("_id"),
                Comment = prop.Value.Description
            });
        }

        return fields;
    }

    private void ExtractMethodsFromPath(string path, OpenApiPathItem pathItem, EntityDefinition entity)
    {
        var operations = new[]
        {
            (OperationType.Get, pathItem.Operations.TryGetValue(OperationType.Get, out var getOp) ? getOp : null),
            (OperationType.Post, pathItem.Operations.TryGetValue(OperationType.Post, out var postOp) ? postOp : null),
            (OperationType.Put, pathItem.Operations.TryGetValue(OperationType.Put, out var putOp) ? putOp : null),
            (OperationType.Patch, pathItem.Operations.TryGetValue(OperationType.Patch, out var patchOp) ? patchOp : null),
            (OperationType.Delete, pathItem.Operations.TryGetValue(OperationType.Delete, out var deleteOp) ? deleteOp : null)
        };

        foreach (var (opType, operation) in operations)
        {
            if (operation == null)
                continue;

            var normalizedPath = NormalizePathParameters(path);
            var methodName = GetMethodName(operation, opType, path);
            var requestType = GetRequestType(operation, pathItem.Parameters, normalizedPath, methodName, entity);
            var responseType = GetResponseType(operation, methodName, entity);

            entity.Methods.Add(new MethodDefinition
            {
                Name = methodName,
                RequestType = requestType,
                ResponseType = responseType,
                HttpMethod = opType.ToString().ToUpper(),
                HttpPath = normalizedPath
            });
        }
    }

    private string NormalizePathParameters(string path)
    {
        // Replace {paramName} with {param_name} to match proto field naming convention
        return System.Text.RegularExpressions.Regex.Replace(path, @"\{([^}]+)\}", match =>
        {
            var paramName = match.Groups[1].Value;
            var snakeCase = ToSnakeCase(paramName);
            return "{" + snakeCase + "}";
        });
    }

    private string GetMethodName(OpenApiOperation operation, OperationType opType, string path)
    {
        // Use operationId if available
        if (!string.IsNullOrEmpty(operation.OperationId))
        {
            return ToPascalCase(operation.OperationId);
        }

        // Generate from HTTP method and path
        var prefix = opType switch
        {
            OperationType.Post => "Create",
            OperationType.Put or OperationType.Patch => "Update",
            OperationType.Delete => "Delete",
            OperationType.Get => path.Contains('{') ? "Get" : "GetAll",
            _ => "Execute"
        };

        var cleanPath = path.Replace("{", "By").Replace("}", "").Replace("/", "");
        return ToPascalCase(prefix + cleanPath);
    }

    private string GetRequestType(OpenApiOperation operation, IList<OpenApiParameter> pathParameters, string path, string methodName, EntityDefinition entity)
    {
        // Combine operation and path parameters
        var allParams = new List<OpenApiParameter>();
        if (operation.Parameters != null) allParams.AddRange(operation.Parameters);
        if (pathParameters != null) allParams.AddRange(pathParameters);

        // Infer parameters from path if they are missing in the spec
        var pathParamMatches = System.Text.RegularExpressions.Regex.Matches(path, @"\{([^}]+)\}");
        foreach (System.Text.RegularExpressions.Match match in pathParamMatches)
        {
            var paramName = match.Groups[1].Value;
            // Check if this path param is already defined in allParams
            // We check snake_case vs snake_case or pascal vs pascal, but paramName here is from normalized path so it is snake_case
            // The existing params might be camelCase from spec

            bool exists = allParams.Any(p => ToSnakeCase(p.Name) == paramName);
            if (!exists)
            {
                allParams.Add(new OpenApiParameter
                {
                    Name = paramName,
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema { Type = "string" },
                    Description = "Auto-generated path parameter"
                });
            }
        }

        if (operation.RequestBody?.Content != null)
        {
            foreach (var content in operation.RequestBody.Content)
            {
                if (content.Value.Schema?.Reference != null)
                {
                    var refName = content.Value.Schema.Reference.Id;
                    return ToPascalCase(refName);
                }

                // Inline schema - create a message for it
                if (content.Value.Schema?.Properties != null)
                {
                    var requestTypeName = methodName + "Request";
                    var fields = ExtractFields(content.Value.Schema);

                    // Add parameters to the request message if they exist
                    if (allParams.Count > 0)
                    {
                        foreach (var param in allParams)
                        {
                            var paramName = ToPascalCase(param.Name);
                            // Avoid duplicates
                            if (!fields.Any(f => f.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase)))
                            {
                                fields.Add(new FieldDefinition
                                {
                                    Name = paramName,
                                    Type = MapOpenApiTypeToCSharp(param.Schema),
                                    Nullable = !param.Required,
                                    Comment = param.Description
                                });
                            }
                        }
                    }

                    entity.Messages.Add(new MessageDefinition
                    {
                        Name = requestTypeName,
                        Fields = fields
                    });

                    return requestTypeName;
                }
            }
        }

        // Check for parameters
        if (allParams.Count > 0)
        {
            var requestTypeName = methodName + "Request";
            var fields = new List<FieldDefinition>();

            foreach (var param in allParams)
            {
                fields.Add(new FieldDefinition
                {
                    Name = ToPascalCase(param.Name),
                    Type = MapOpenApiTypeToCSharp(param.Schema),
                    Nullable = !param.Required,
                    Comment = param.Description
                });
            }

            entity.Messages.Add(new MessageDefinition
            {
                Name = requestTypeName,
                Fields = fields
            });

            return requestTypeName;
        }

        // Default empty request
        return "google.protobuf.Empty";
    }

    private string GetResponseType(OpenApiOperation operation, string methodName, EntityDefinition entity)
    {
        // Look for 2xx responses
        var successResponse = operation.Responses.FirstOrDefault(r => r.Key.StartsWith("2")).Value;

        if (successResponse?.Content != null)
        {
            foreach (var content in successResponse.Content)
            {
                // Array response
                if (content.Value.Schema?.Type == "array" && content.Value.Schema.Items?.Reference != null)
                {
                    var itemType = ToPascalCase(content.Value.Schema.Items.Reference.Id);
                    var responseTypeName = "Res" + itemType + "All";

                    // Create response message if not exists
                    if (!entity.Messages.Any(m => m.Name == responseTypeName))
                    {
                        entity.Messages.Add(new MessageDefinition
                        {
                            Name = responseTypeName,
                            Fields = new List<FieldDefinition>
                            {
                                new FieldDefinition
                                {
                                    Name = "List" + itemType,
                                    Type = itemType,
                                    IsRepeated = true,
                                    Nullable = false
                                }
                            }
                        });
                    }

                    return responseTypeName;
                }

                // Single object response
                if (content.Value.Schema?.Reference != null)
                {
                    return ToPascalCase(content.Value.Schema.Reference.Id);
                }

                // Inline schema
                if (content.Value.Schema?.Properties != null)
                {
                    var responseTypeName = methodName + "Response";
                    var fields = ExtractFields(content.Value.Schema);

                    entity.Messages.Add(new MessageDefinition
                    {
                        Name = responseTypeName,
                        Fields = fields
                    });

                    return responseTypeName;
                }
            }
        }

        // Default empty response
        return "google.protobuf.Empty";
    }

    private string MapOpenApiTypeToCSharp(OpenApiSchema schema)
    {
        if (schema.Type == "array" && schema.Items != null)
        {
            var itemType = MapOpenApiTypeToCSharp(schema.Items);
            return itemType; // IsRepeated flag will handle the array part
        }

        return schema.Type switch
        {
            "string" => schema.Format switch
            {
                "date-time" => "DateTime",
                "date" => "DateOnly",
                "uuid" => "Guid",
                _ => "string"
            },
            "integer" => schema.Format == "int64" ? "long" : "int",
            "number" => schema.Format == "float" ? "float" : "double",
            "boolean" => "bool",
            "object" => "string", // JSON string representation
            _ => "string"
        };
    }

    private string GenerateProtoContent(EntityDefinition entity)
    {
        var sb = new StringBuilder();
        sb.AppendLine("syntax = \"proto3\";");
        sb.AppendLine();
        sb.AppendLine("import \"google/protobuf/wrappers.proto\";");
        sb.AppendLine("import \"google/protobuf/timestamp.proto\";");
        sb.AppendLine("import \"google/protobuf/empty.proto\";");
        sb.AppendLine("import \"google/api/annotations.proto\";");
        sb.AppendLine();
        sb.AppendLine($"package {ToSnakeCase(entity.Name)};");
        sb.AppendLine();

        // Generate messages
        foreach (var message in entity.Messages)
        {
            sb.AppendLine($"message {message.Name} {{");
            int fieldNumber = 1;
            foreach (var field in message.Fields)
            {
                var repeated = field.IsRepeated ? "repeated " : "";
                var protoType = GetProtoType(field.Type, field.Nullable);
                sb.AppendLine($"    {repeated}{protoType} {ToSnakeCase(field.Name)} = {fieldNumber};");
                fieldNumber++;
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Generate service
        sb.AppendLine($"service {entity.ServiceName} {{");
        foreach (var method in entity.Methods)
        {
            var reqType = method.RequestType == "google.protobuf.Empty"
                ? "google.protobuf.Empty"
                : method.RequestType;
            var resType = method.ResponseType == "google.protobuf.Empty"
                ? "google.protobuf.Empty"
                : method.ResponseType;

            sb.AppendLine($"    rpc {method.Name}({reqType}) returns ({resType}) {{");
            sb.AppendLine($"        option (google.api.http) = {{");
            sb.AppendLine($"            {(method.HttpMethod ?? "get").ToLower()}: \"{method.HttpPath}\"");
            if (method.HttpMethod != "GET" && method.HttpMethod != "DELETE" && reqType != "google.protobuf.Empty")
            {
                sb.AppendLine($"            body: \"*\"");
            }
            sb.AppendLine($"        }};");
            sb.AppendLine($"    }}");
        }
        sb.AppendLine("}");

        return sb.ToString();
    }

    private string GetProtoType(string csharpType, bool nullable)
    {
        // Strip nullable suffix if present to get the base type for mapping
        var typeToMap = csharpType.TrimEnd('?');

        var baseType = typeToMap switch
        {
            "string" => "string",
            "int" => "int32",
            "long" => "int64",
            "float" => "float",
            "double" => "double",
            "bool" => "bool",
            "DateTime" => "google.protobuf.Timestamp",
            _ => typeToMap // Custom types
        };

        // Wrap in google.protobuf wrapper for nullable types
        if (nullable && baseType != "google.protobuf.Timestamp")
        {
            return baseType switch
            {
                "string" => "google.protobuf.StringValue",
                "int32" => "google.protobuf.Int32Value",
                "int64" => "google.protobuf.Int64Value",
                "float" => "google.protobuf.FloatValue",
                "double" => "google.protobuf.DoubleValue",
                "bool" => "google.protobuf.BoolValue",
                _ => baseType
            };
        }

        return baseType;
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Handle snake_case, kebab-case, and spaces
        var words = input.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        // If only one word and it's already mixed case, preserve it
        if (words.Length == 1 && input.Any(char.IsUpper) && input.Any(char.IsLower))
        {
            // Just ensure first letter is uppercase
            return char.ToUpper(input[0]) + input.Substring(1);
        }

        // Convert each word to PascalCase
        return string.Concat(words.Select(word =>
        {
            if (string.IsNullOrEmpty(word))
                return "";

            // If the word provides mixed case (e.g. "CityRide"), preserve it but ensure first char is upper
            if (word.Any(char.IsLower) && word.Any(char.IsUpper))
            {
                return char.ToUpper(word[0]) + word.Substring(1);
            }

            // Otherwise (all caps or all lower), normalize to TitleCase
            return char.ToUpper(word[0]) + word.Substring(1).ToLower();
        }));
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = new StringBuilder();
        result.Append(char.ToLower(input[0]));

        for (int i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]))
            {
                result.Append('_');
                result.Append(char.ToLower(input[i]));
            }
            else
            {
                result.Append(input[i]);
            }
        }

        return result.ToString();
    }
    private bool IsValueType(string type)
    {
        return type is "int" or "long" or "float" or "double" or "bool" or "DateTime" or "DateOnly" or "Guid";
    }
}
