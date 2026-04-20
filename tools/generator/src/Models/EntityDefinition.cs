using System.Collections.Generic;

namespace SkeletonApi.Generator.Models;

public class EntityDefinition
{
    public required string Name { get; set; }
    public required string ServiceName { get; set; }
    public required string TableName { get; set; }
    public required string ProtoMessageName { get; set; }
    public List<FieldDefinition> Fields { get; set; } = new();
    public List<MethodDefinition> Methods { get; set; } = new();
    public List<MessageDefinition> Messages { get; set; } = new();
    public List<EnumDefinition> Enums { get; set; } = new();
    public required string ProtoContent { get; set; }
    public string? SourcePath { get; set; }
}

public class EnumDefinition
{
    public required string Name { get; set; }
    public string? ProtoName { get; set; }
    public List<string> Values { get; set; } = new();
}

public class MessageDefinition
{
    public required string Name { get; set; }
    public string? ProtoName { get; set; }
    public List<FieldDefinition> Fields { get; set; } = new();
}

public class MethodDefinition
{
    public required string Name { get; set; }
    public required string RequestType { get; set; }
    public required string ResponseType { get; set; }
    public string? ProtoRequestType { get; set; }
    public string? ProtoResponseType { get; set; }
    public string? HttpMethod { get; set; }
    public string? HttpPath { get; set; }
}

public class FieldDefinition
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public bool Nullable { get; set; }
    public bool IsRepeated { get; set; }
    public bool PrimaryKey { get; set; }
    public string? Comment { get; set; }
}
