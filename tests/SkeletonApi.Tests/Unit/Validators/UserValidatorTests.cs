using FluentAssertions;
using SkeletonApi.Application.Validators;
using SkeletonApi.Domain.Entities;
using Xunit;

namespace SkeletonApi.Tests.Unit.Validators;

public class UserValidatorTests
{
    private readonly UserValidator _validator;

    public UserValidatorTests()
    {
        _validator = new UserValidator();
    }

    [Fact]
    public async Task Validate_WithValidUser_ShouldPass()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = "password123",
            FullName = "Test User",
            Role = "user"
        };

        // Act
        var result = await _validator.ValidateAsync(user);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "test@example.com", "password123", "Test User")]
    [InlineData("testuser", "", "password123", "Test User")]
    [InlineData("testuser", "test@example.com", "", "Test User")]
    [InlineData("testuser", "test@example.com", "password123", "")]
    public async Task Validate_WithMissingRequiredFields_ShouldFail(
        string username, string email, string password, string fullName)
    {
        // Arrange
        var user = new User
        {
            Username = username,
            Email = email,
            Password = password,
            FullName = fullName
        };

        // Act
        var result = await _validator.ValidateAsync(user);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("@example.com")]
    [InlineData("test@")]
    [InlineData("test")]
    public async Task Validate_WithInvalidEmail_ShouldFail(string email)
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = email,
            Password = "password123",
            FullName = "Test User"
        };

        // Act
        var result = await _validator.ValidateAsync(user);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Theory]
    [InlineData("ab")]  // Too short
    [InlineData("a")]   // Too short
    public async Task Validate_WithShortUsername_ShouldFail(string username)
    {
        // Arrange
        var user = new User
        {
            Username = username,
            Email = "test@example.com",
            Password = "password123",
            FullName = "Test User"
        };

        // Act
        var result = await _validator.ValidateAsync(user);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Username");
    }

    [Theory]
    [InlineData("12345")]  // Too short
    [InlineData("pass")]   // Too short
    public async Task Validate_WithShortPassword_ShouldFail(string password)
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = password,
            FullName = "Test User"
        };

        // Act
        var result = await _validator.ValidateAsync(user);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }
}
