using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Moq;
using SkeletonApi.Application.Interfaces;
using SkeletonApi.Application.Services;
using SkeletonApi.Common.Errors;
using SkeletonApi.Domain.Entities;
using Xunit;

namespace SkeletonApi.Tests.Unit.Services;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<ICacheRepository> _mockCacheRepository;
    private readonly Mock<ILogger<UserService>> _mockLogger;
    private readonly Mock<IValidator<User>> _mockValidator;
    private readonly Mock<SkeletonApi.Common.Concurrency.ISingleFlight> _mockSingleFlight;
    private readonly UserService _userService;

    public UserServiceTests()
    {
        _mockUserRepository = new Mock<IUserRepository>();
        _mockCacheRepository = new Mock<ICacheRepository>();
        _mockLogger = new Mock<ILogger<UserService>>();
        _mockValidator = new Mock<IValidator<User>>();
        _mockSingleFlight = new Mock<SkeletonApi.Common.Concurrency.ISingleFlight>();

        _userService = new UserService(
            _mockUserRepository.Object,
            _mockCacheRepository.Object,
            _mockLogger.Object,
            _mockValidator.Object,
            singleFlight: _mockSingleFlight.Object
        );
    }

    [Fact]
    public async Task CreateAsync_WithValidUser_ShouldCreateUserSuccessfully()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = "password123",
            FullName = "Test User"
        };

        _mockValidator.Setup(v => v.ValidateAsync(It.IsAny<User>(), default))
            .ReturnsAsync(new ValidationResult());

        _mockUserRepository.Setup(r => r.GetByEmailAsync(user.Email))
            .ReturnsAsync((User?)null);

        _mockUserRepository.Setup(r => r.CreateAsync(It.IsAny<User>()))
            .Returns(Task.CompletedTask);

        _mockCacheRepository.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockCacheRepository.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<User>(), It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _userService.CreateAsync(user);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrEmpty();
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.IsActive.Should().BeTrue();

        _mockUserRepository.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Once);
        _mockCacheRepository.Verify(c => c.RemoveAsync("users:all"), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidUser_ShouldThrowValidationException()
    {
        // Arrange
        var user = new User { Username = "", Email = "invalid" };

        var validationFailures = new List<ValidationFailure>
        {
            new ValidationFailure("Username", "Username is required"),
            new ValidationFailure("Email", "Invalid email format")
        };

        _mockValidator.Setup(v => v.ValidateAsync(It.IsAny<User>(), default))
            .ReturnsAsync(new ValidationResult(validationFailures));

        // Act & Assert
        var act = () => _userService.CreateAsync(user);
        await act.Should().ThrowAsync<FluentValidation.ValidationException>();

        _mockUserRepository.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithExistingEmail_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = "existing@example.com",
            Password = "password123"
        };

        var existingUser = new User { Id = "existing-id", Email = "existing@example.com" };

        _mockValidator.Setup(v => v.ValidateAsync(It.IsAny<User>(), default))
            .ReturnsAsync(new ValidationResult());

        _mockUserRepository.Setup(r => r.GetByEmailAsync(user.Email))
            .ReturnsAsync(existingUser);

        // Act & Assert
        var act = () => _userService.CreateAsync(user);
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage($"User with email {user.Email} already exists.");

        _mockUserRepository.Verify(r => r.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_WithCachedUser_ShouldReturnFromCache()
    {
        // Arrange
        var userId = "test-id";
        var cachedUser = new User { Id = userId, Username = "cached" };

        _mockSingleFlight.Setup(sf => sf.DoAsync(It.IsAny<string>(), It.IsAny<Func<Task<User?>>>()))
            .Returns<string, Func<Task<User?>>>((key, func) => func());

        _mockCacheRepository.Setup(c => c.GetFromReplicaAsync<User>(It.IsAny<string>()))
            .ReturnsAsync(cachedUser);

        // Act
        var result = await _userService.GetByIdAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(userId);
        result.Username.Should().Be("cached");

        _mockUserRepository.Verify(r => r.GetByIdAsync(It.IsAny<string>()), Times.Never);
        _mockCacheRepository.Verify(c => c.GetFromReplicaAsync<User>(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WithoutCache_ShouldFetchFromRepository()
    {
        // Arrange
        var userId = "test-id";
        var dbUser = new User { Id = userId, Username = "fromdb" };

        _mockSingleFlight.Setup(sf => sf.DoAsync(It.IsAny<string>(), It.IsAny<Func<Task<User?>>>()))
            .Returns<string, Func<Task<User?>>>((key, func) => func());

        _mockCacheRepository.Setup(c => c.GetFromReplicaAsync<User>(It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        _mockUserRepository.Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(dbUser);

        _mockCacheRepository.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<User>(), It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _userService.GetByIdAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(userId);
        result.Username.Should().Be("fromdb");

        _mockUserRepository.Verify(r => r.GetByIdAsync(userId), Times.Once);
        _mockCacheRepository.Verify(c => c.SetAsync($"user:{userId}", dbUser, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveUserAndInvalidateCache()
    {
        // Arrange
        var userId = "test-id";

        _mockUserRepository.Setup(r => r.DeleteAsync(userId))
            .Returns(Task.CompletedTask);

        _mockCacheRepository.Setup(c => c.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _userService.DeleteAsync(userId);

        // Assert
        _mockUserRepository.Verify(r => r.DeleteAsync(userId), Times.Once);
        _mockCacheRepository.Verify(c => c.RemoveAsync($"user:{userId}"), Times.Once);
        _mockCacheRepository.Verify(c => c.RemoveAsync("users:all"), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ShouldUseSingleFlightPattern()
    {
        // Arrange
        var query = "test";
        var users = new List<User>
        {
            new User { Id = "1", Username = "test1" },
            new User { Id = "2", Username = "test2" }
        };

        _mockSingleFlight.Setup(sf => sf.DoAsync(It.IsAny<string>(), It.IsAny<Func<Task<IEnumerable<User>>>>()))
            .Returns<string, Func<Task<IEnumerable<User>>>>((key, func) => func());

        _mockUserRepository.Setup(r => r.SearchAsync(query))
            .ReturnsAsync(users);

        // Act
        var result = await _userService.SearchAsync(query);

        // Assert
        result.Should().HaveCount(2);
        _mockSingleFlight.Verify(sf => sf.DoAsync($"search_users:{query}", It.IsAny<Func<Task<IEnumerable<User>>>>()), Times.Once);
        _mockUserRepository.Verify(r => r.SearchAsync(query), Times.Once);
    }
}
