using System;
using System.IO;
using Newtonsoft.Json;
using SkeletonApi.Generator.Models;

namespace SkeletonApi.Generator.Services;

public class InputParser
{
    private readonly ProtoParser _protoParser = new();
    private readonly OpenApiParser _openApiParser = new();
    private readonly SqlParser _sqlParser = new();

    public EntityDefinition Parse(string filePath, string type)
    {
        var content = File.ReadAllText(filePath);

        var entity = type.ToLower() switch
        {
            "json" => ParseJson(content),
            "proto" => _protoParser.Parse(content),
            "openapi" or "yaml" or "yml" => _openApiParser.Parse(content),
            "schema" or "sql" => _sqlParser.Parse(content),
            _ => throw new ArgumentException($"Unsupported input type: {type}")
        };

        entity.SourcePath = Path.GetFullPath(filePath);
        return entity;
    }


    private EntityDefinition ParseJson(string content)
    {
        return JsonConvert.DeserializeObject<EntityDefinition>(content)
            ?? throw new InvalidOperationException("Failed to parse JSON");
    }
}
