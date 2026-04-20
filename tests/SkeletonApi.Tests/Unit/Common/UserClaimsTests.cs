using System.Text.Json;
using SkeletonApi.Common.Models;
using Xunit;

namespace SkeletonApi.Tests.Unit.Common;

public class UserClaimsTests
{
    [Fact]
    public void SerializeExtraAttributes_ShouldReturnJson_WhenExtraAttributesExist()
    {
        // Arrange
        var claims = new UserClaims
        {
            UserId = "user-123",
            ExtraAttributes = new Dictionary<string, object>
            {
                { "department", "Engineering" },
                { "level", 5 },
                { "metadata", new Dictionary<string, object> { { "team", "Backend" } } }
            }
        };

        // Act
        var json = claims.SerializeExtraAttributes();

        // Assert
        Assert.NotNull(json);
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, object>>(json!);
        Assert.NotNull(deserialized);
        Assert.Equal("Engineering", deserialized!["department"]?.ToString());
        Assert.Equal("5", deserialized["level"]?.ToString());
    }

    [Fact]
    public void SerializeExtraAttributes_ShouldReturnNull_WhenEmpty()
    {
        // Arrange
        var claims = new UserClaims { UserId = "user-123" };

        // Act
        var json = claims.SerializeExtraAttributes();

        // Assert
        Assert.Null(json);
    }

    [Fact]
    public void DeserializeExtraAttributes_ShouldReturnDictionary_WhenJsonIsValid()
    {
        // Arrange
        var json = "{\"department\":\"Engineering\",\"level\":5,\"metadata\":{\"team\":\"Backend\"}}";

        // Act
        var extra = UserClaims.DeserializeExtraAttributes(json);

        // Assert
        Assert.NotNull(extra);
        Assert.Equal("Engineering", extra!["department"].ToString());
        Assert.Equal("5", extra["level"].ToString());

        Assert.NotNull(extra["metadata"]);
        var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(extra["metadata"]!.ToString()!);
        Assert.NotNull(metadata);
        Assert.Equal("Backend", metadata!["team"].ToString());
    }

    [Fact]
    public void DeserializeExtraAttributes_ShouldReturnNull_WhenJsonIsInvalid()
    {
        // Arrange
        var json = "invalid-json";

        // Act
        var extra = UserClaims.DeserializeExtraAttributes(json);

        // Assert
        Assert.Null(extra);
    }

    [Fact]
    public void BackwardCompatibility_ShouldWork()
    {
        // Arrange
        var claims = new UserClaims { UserId = "user-123" };

        // Assert
        Assert.Equal("user-123", claims.Id);
        Assert.Equal("user-123", claims.Username);

        // Act
        claims.Id = "new-id";
        Assert.Equal("new-id", claims.UserId);

        // Act
        claims.Username = "new-username";
        Assert.Equal("new-username", claims.UserId);
    }
}
