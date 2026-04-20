using System.Text.RegularExpressions;
using SkeletonApi.Generator.Models;

namespace SkeletonApi.Generator.Services;

public class ProtoParser
{
    private HashSet<string> _allTypeNames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _typeNameToProtoName = new(StringComparer.OrdinalIgnoreCase);

    public EntityDefinition Parse(string content)
    {
        var entity = new EntityDefinition
        {
            ProtoContent = content,
            Name = string.Empty,
            ServiceName = string.Empty,
            TableName = string.Empty,
            ProtoMessageName = string.Empty
        };

        // Pre-process: remove comments
        var cleanContent = Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);
        cleanContent = Regex.Replace(cleanContent, @"//.*", "");

        // Pre-collect all potential message/enum names for resolution
        _allTypeNames.Clear();
        _typeNameToProtoName.Clear();
        CollectNamesRecursive(cleanContent, string.Empty, string.Empty);

        ParseRecursive(cleanContent, string.Empty, string.Empty, entity);

        if (entity.Messages.Any())
        {
            // Use first message as main entity if not already set
            var firstMsg = entity.Messages.Find(m => !m.Name.Contains('_')) ?? entity.Messages.First();
            entity.Name = firstMsg.Name;
            entity.ProtoMessageName = entity.Name;
            entity.TableName = ToSnakeCase(entity.Name) + "s";
            entity.Fields = firstMsg.Fields;
        }

        // Parse service name
        var serviceMatch = Regex.Match(cleanContent, @"service\s+(\w+)\s*\{");
        if (serviceMatch.Success)
        {
            entity.ServiceName = serviceMatch.Groups[1].Value;
        }
        else
        {
            entity.ServiceName = entity.Name + "GrpcService";
        }

        // Parse service RPCs for methods
        var rpcMatches = Regex.Matches(cleanContent, @"rpc\s+(\w+)\s*\(([^)]+)\)\s*returns\s*\(([^)]+)\)");
        foreach (Match rpcMatch in rpcMatches)
        {
            var reqType = rpcMatch.Groups[2].Value.Trim();
            var resType = rpcMatch.Groups[3].Value.Trim();
            var resolvedReq = ResolveTypeName(reqType, string.Empty, entity);
            var resolvedRes = ResolveTypeName(resType, string.Empty, entity);

            entity.Methods.Add(new MethodDefinition
            {
                Name = rpcMatch.Groups[1].Value,
                RequestType = resolvedReq,
                ResponseType = resolvedRes,
                ProtoRequestType = ResolveProtoName(resolvedReq),
                ProtoResponseType = ResolveProtoName(resolvedRes)
            });
        }

        return entity;
    }

    private string ResolveProtoName(string resolvedType)
    {
        if (resolvedType == "Empty" || resolvedType == "google.protobuf.Empty") return "Empty";
        if (resolvedType.StartsWith("google.protobuf.")) return resolvedType.Replace("google.protobuf.", "");

        return _typeNameToProtoName.TryGetValue(resolvedType, out var protoName) ? protoName : resolvedType;
    }

    private void CollectNamesRecursive(string content, string prefix, string protoPrefix)
    {
        foreach (var (name, body, type) in ExtractBlocks(content))
        {
            var fullName = string.IsNullOrEmpty(prefix) ? name : $"{prefix}_{name}";
            var fullProtoName = string.IsNullOrEmpty(protoPrefix) ? name : $"{protoPrefix}.Types.{name}";

            _allTypeNames.Add(fullName);
            _typeNameToProtoName[fullName] = fullProtoName;

            if (type == "message")
            {
                CollectNamesRecursive(body, fullName, fullProtoName);
            }
        }
    }

    private void ParseRecursive(string content, string prefix, string protoPrefix, EntityDefinition entity)
    {
        foreach (var (name, body, type) in ExtractBlocks(content))
        {
            var fullName = string.IsNullOrEmpty(prefix) ? name : $"{prefix}_{name}";
            var fullProtoName = string.IsNullOrEmpty(protoPrefix) ? name : $"{protoPrefix}.Types.{name}";

            if (type == "message")
            {
                var messageDef = new MessageDefinition
                {
                    Name = fullName,
                    ProtoName = fullProtoName
                };
                var cleanBody = StripNestedBlocks(body);
                var fieldMatches = Regex.Matches(cleanBody, @"(repeated\s+|optional\s+)?([\w\.]+)\s+(\w+)\s*=\s*\d+[^;]*;");

                foreach (Match fieldMatch in fieldMatches)
                {
                    var isRepeated = fieldMatch.Groups[1].Value.Contains("repeated");
                    var protoType = fieldMatch.Groups[2].Value;
                    var fieldName = fieldMatch.Groups[3].Value;
                    var resolvedType = ResolveTypeName(protoType, fullName, entity);

                    messageDef.Fields.Add(new FieldDefinition
                    {
                        Name = ToPascalCase(fieldName),
                        Type = MapProtoTypeToCSharp(resolvedType),
                        Nullable = protoType.Contains("Value") && protoType.StartsWith("google.protobuf."),
                        IsRepeated = isRepeated
                    });
                }
                entity.Messages.Add(messageDef);

                // Recurse for nested blocks
                ParseRecursive(body, fullName, fullProtoName, entity);
            }
            else if (type == "enum")
            {
                var enumDef = new EnumDefinition
                {
                    Name = fullName,
                    ProtoName = fullProtoName
                };
                var valueMatches = Regex.Matches(body, @"(\w+)\s*=\s*\d+;");
                foreach (Match match in valueMatches)
                {
                    enumDef.Values.Add(match.Groups[1].Value);
                }
                entity.Enums.Add(enumDef);
            }
        }
    }

    private string ResolveTypeName(string type, string currentPrefix, EntityDefinition entity)
    {
        var clean = type.Replace("google.protobuf.", "");
        if (IsPrimitive(clean)) return clean;

        // Try to resolve by going up the prefix chain
        var parts = currentPrefix.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length; i >= 0; i--)
        {
            var prefix = string.Join("_", parts.Take(i));
            var candidate = string.IsNullOrEmpty(prefix) ? clean : $"{prefix}_{clean}";

            if (_allTypeNames.Contains(candidate))
            {
                return candidate;
            }
        }

        // HEURISTIC: Check common patterns if not resolved
        if (clean == "Data" || clean == "OrderHeader" || clean == "OrderDetail" ||
            clean == "OrderPayment" || clean == "ItemDetail" || clean == "PaymentInfo" || clean == "BookingType" ||
            clean == "ResDataGeneral" || clean == "GeneralMessageResponse")
        {
            // Fallback to current prefix if it looks like a match
            if (!string.IsNullOrEmpty(currentPrefix)) return $"{currentPrefix}_{clean}";
        }

        return clean;
    }

    private bool IsPrimitive(string type)
    {
        return type switch
        {
            "string" or "int32" or "uint32" or "sint32" or "fixed32" or "sfixed32" or
            "int64" or "uint64" or "sint64" or "fixed64" or "sfixed64" or
            "bool" or "double" or "float" or "bytes" or "Timestamp" or
            "StringValue" or "Int32Value" or "Int64Value" or "BoolValue" or "DoubleValue" or "FloatValue" or "Empty" => true,
            _ => false
        };
    }

    private string StripNestedBlocks(string body)
    {
        var result = body;
        while (true)
        {
            var match = Regex.Match(result, @"\b(message|enum)\s+\w+\s*\{");
            if (!match.Success) break;

            var startBrace = match.Index + match.Value.Length;
            int count = 1;
            int endBrace = -1;
            for (int i = startBrace; i < result.Length; i++)
            {
                if (result[i] == '{') count++;
                else if (result[i] == '}') count--;
                if (count == 0) { endBrace = i; break; }
            }

            if (endBrace != -1)
            {
                result = result.Remove(match.Index, endBrace - match.Index + 1);
            }
            else break;
        }
        return result;
    }

    private IEnumerable<(string Name, string Body, string Type)> ExtractBlocks(string content)
    {
        int i = 0;
        while (i < content.Length)
        {
            var match = Regex.Match(content.Substring(i), @"\b(message|enum)\s+(\w+)\s*\{");
            if (!match.Success) break;

            var type = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            var startBrace = i + match.Index + match.Value.Length;

            int count = 1;
            int endBrace = -1;
            for (int j = startBrace; j < content.Length; j++)
            {
                if (content[j] == '{') count++;
                else if (content[j] == '}') count--;
                if (count == 0) { endBrace = j; break; }
            }

            if (endBrace != -1)
            {
                var body = content.Substring(startBrace, endBrace - startBrace);
                yield return (name, body, type);
                i = endBrace + 1;
            }
            else
            {
                i = startBrace + 1;
            }
        }
    }

    private string MapProtoTypeToCSharp(string type)
    {
        return type switch
        {
            "StringValue" => "string",
            "string" => "string",
            "int32" => "int",
            "uint32" => "uint",
            "sint32" => "int",
            "fixed32" => "uint",
            "sfixed32" => "int",
            "int64" => "long",
            "uint64" => "ulong",
            "sint64" => "long",
            "fixed64" => "ulong",
            "sfixed64" => "long",
            "Int32Value" => "int?",
            "Int64Value" => "long?",
            "UInt32Value" => "uint?",
            "UInt64Value" => "ulong?",
            "BoolValue" => "bool?",
            "bool" => "bool",
            "DoubleValue" => "double?",
            "double" => "double",
            "FloatValue" => "float?",
            "float" => "float",
            "Timestamp" => "DateTime",
            "bytes" => "byte[]",
            "Empty" => "void",
            _ => type
        };
    }

    private string ToPascalCase(string snakeCase)
    {
        return string.Join("", snakeCase.Split('_').Select(w =>
            char.ToUpper(w[0]) + w.Substring(1).ToLower()));
    }

    private string ToSnakeCase(string pascalCase)
    {
        return Regex.Replace(pascalCase, "([a-z])([A-Z])", "$1_$2").ToLower();
    }
}
