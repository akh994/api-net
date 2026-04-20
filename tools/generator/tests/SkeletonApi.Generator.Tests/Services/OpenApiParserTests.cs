using FluentAssertions;
using SkeletonApi.Generator.Services;
using Xunit;

namespace SkeletonApi.Generator.Tests.Services;

public class OpenApiParserTests
{
    private readonly OpenApiParser _parser;

    public OpenApiParserTests()
    {
        _parser = new OpenApiParser();
    }

    [Fact]
    public void Parse_ShouldHandleMixedCaseEntityNames()
    {
        // Arrange
        var yaml = @"
openapi: 3.0.0
info:
  title: CityRide Taxi API
  version: 1.0.0
paths:
  /taxi:
    get:
      responses:
        '200':
          description: OK
components:
  schemas:
    CityRideTaxi:
      type: object
      properties:
        id:
          type: string
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        // The parser uses the title to determine the main entity name if not provided
        // "CityRide Taxi API" -> "CityRideTaxi"
        result.Name.Should().Be("CityRideTaxi"); 
    }

    [Fact]
    public void Parse_ShouldMapTypesCorrectly()
    {
        // Arrange
        var yaml = @"
openapi: 3.0.0
info:
  title: Test API
  version: 1.0.0
paths: {}
components:
  schemas:
    TestEntity:
      type: object
      required: [requiredInt]
      properties:
        id:
          type: string
        requiredInt:
          type: integer
        optionalInt:
          type: integer
        longValue:
          type: integer
          format: int64
        dateTime:
          type: string
          format: date-time
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        var fields = result.Fields;
        
        // Use single assertions for clarity
        fields.Should().Contain(f => f.Name == "RequiredInt" && f.Type == "int" && !f.Nullable, "Required int should be non-nullable int");
        fields.Should().Contain(f => f.Name == "OptionalInt" && f.Type == "int?" && f.Nullable, "Optional int should be nullable int?");
        fields.Should().Contain(f => f.Name == "LongValue" && f.Type == "long?" && f.Nullable, "Long should be nullable long? if not required");
        fields.Should().Contain(f => f.Name == "DateTime" && f.Type == "DateTime?" && f.Nullable, "DateTime should be nullable DateTime? if not required");
    }

    [Fact]
    public void Parse_ShouldGenerateCorrectProto()
    {
        // Arrange
        var yaml = @"
openapi: 3.0.0
info:
  title: Proto Test
  version: 1.0.0
paths: {}
components:
  schemas:
    User:
      type: object
      properties:
        id:
          type: string
        age:
          type: integer
        score:
          type: integer
          format: int64
        isActive:
          type: boolean
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        var proto = result.ProtoContent;
        proto.Should().Contain("google.protobuf.Int32Value age =");
        proto.Should().NotContain("int32 age =", "Should not generate primitive for nullable field"); 
        proto.Should().Contain("google.protobuf.Int64Value score =");
        proto.Should().Contain("google.protobuf.BoolValue is_active =");
    }
}
