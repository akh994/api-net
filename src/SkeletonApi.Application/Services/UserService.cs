using FluentValidation;
using Microsoft.Extensions.Logging;
using SkeletonApi.Application.Interfaces;
using SkeletonApi.Common.Errors;
using SkeletonApi.Domain.Entities;

namespace SkeletonApi.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ICacheRepository _cacheRepository;
    private readonly ILogger<UserService> _logger;
    private readonly IValidator<User> _validator;
    private readonly IUserMessagePublisher? _messagePublisher;
    private readonly ISseManager? _sseManager;
    private readonly SkeletonApi.Common.FeatureFlags.FeatureFlagService? _featureFlagService;
    private readonly IUserGrpcRepository? _grpcRepository;
    private readonly IUserRestRepository? _restRepository;
    private readonly SkeletonApi.Common.Concurrency.ISingleFlight _singleFlight;

    public UserService(
        IUserRepository userRepository,
        ICacheRepository cacheRepository,
        ILogger<UserService> logger,
        IValidator<User> validator,
        IUserMessagePublisher? messagePublisher = null,
        ISseManager? sseManager = null,
        SkeletonApi.Common.FeatureFlags.FeatureFlagService? featureFlagService = null,
        IUserGrpcRepository? grpcRepository = null,
        IUserRestRepository? restRepository = null,
        SkeletonApi.Common.Concurrency.ISingleFlight? singleFlight = null)
    {
        _userRepository = userRepository;
        _cacheRepository = cacheRepository;
        _logger = logger;
        _validator = validator;
        _messagePublisher = messagePublisher;
        _sseManager = sseManager;
        _featureFlagService = featureFlagService;
        _grpcRepository = grpcRepository;
        _restRepository = restRepository;
        _singleFlight = singleFlight ?? new SkeletonApi.Common.Concurrency.SingleFlight();
    }

    public async Task<User> CreateAsync(User user)
    {
        _logger.LogInformation("Creating user: {Username}", user.Username);

        var validationResult = await _validator.ValidateAsync(user);
        if (!validationResult.IsValid)
        {
            throw new FluentValidation.ValidationException(validationResult.Errors);
        }

        user.Id = Guid.NewGuid().ToString();
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        user.IsActive = true;

        // Check if email exists
        var existingUser = await _userRepository.GetByEmailAsync(user.Email);
        if (existingUser != null)
        {
            throw new ConflictException($"User with email {user.Email} already exists.");
        }

        // Hash password (TODO: Implement proper hashing)
        // user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);

        await _userRepository.CreateAsync(user);

        // Invalidate cache
        await _cacheRepository.RemoveAsync("users:all");
        await _cacheRepository.SetAsync($"user:{user.Id}", user, TimeSpan.FromMinutes(10));


        // Publish user.created event (best effort - don't fail request if publish fails)
        if (_messagePublisher != null)
        {
            await _messagePublisher.PublishUserCreatedAsync(user);
        }

        _logger.LogInformation("User created successfully: {UserId}", user.Id);
        return user;
    }

    public async Task DeleteAsync(string id)
    {
        _logger.LogInformation("Deleting user: {UserId}", id);
        await _userRepository.DeleteAsync(id);
        await _cacheRepository.RemoveAsync($"user:{id}");
        await _cacheRepository.RemoveAsync("users:all");

        // Publish user.deleted event (best effort)
        if (_messagePublisher != null)
        {
            await _messagePublisher.PublishUserDeletedAsync(id);
        }

        _logger.LogInformation("User deleted successfully: {UserId}", id);
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        return await _singleFlight.DoAsync("get_all_users", async () =>
        {
            var cacheKey = "users:all";
            // Read from replica for better performance
            var cachedUsers = await _cacheRepository.GetFromReplicaAsync<IEnumerable<User>>(cacheKey);

            if (cachedUsers != null)
            {
                return cachedUsers;
            }

            var users = await _userRepository.GetAllAsync();
            await _cacheRepository.SetAsync(cacheKey, users, TimeSpan.FromMinutes(5));

            return users;
        });
    }

    public async Task<(IEnumerable<User> Items, int TotalItems)> GetAllPaginatedAsync(int page, int pageSize)
    {
        // Caching paginated results might be tricky due to many combinations
        // For now, we just fetch from DB
        return await _userRepository.GetAllPaginatedAsync(page, pageSize);
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        return await _singleFlight.DoAsync($"get_user_by_id:{id}", async () =>
        {
            var cacheKey = $"user:{id}";
            // Read from replica for better performance
            var cachedUser = await _cacheRepository.GetFromReplicaAsync<User>(cacheKey);

            if (cachedUser != null)
            {
                return cachedUser;
            }

            var user = await _userRepository.GetByIdAsync(id);
            if (user != null)
            {
                await _cacheRepository.SetAsync(cacheKey, user, TimeSpan.FromMinutes(10));
            }

            return user;
        });
    }

    public async Task<IEnumerable<User>> SearchAsync(string query)
    {
        return await _singleFlight.DoAsync($"search_users:{query}", async () =>
        {
            return await _userRepository.SearchAsync(query);
        });
    }

    public async Task UpdateAsync(string id, User user)
    {
        _logger.LogInformation("Updating user: {UserId}", id);

        var existingUser = await _userRepository.GetByIdAsync(id);
        if (existingUser == null)
        {
            throw new NotFoundException($"User with ID {id} not found.");
        }

        user.Id = id;
        user.UpdatedAt = DateTime.UtcNow;

        // Preserve fields that shouldn't change if not provided
        if (string.IsNullOrEmpty(user.Password)) user.Password = existingUser.Password;
        if (string.IsNullOrEmpty(user.Username)) user.Username = existingUser.Username;
        if (string.IsNullOrEmpty(user.Email)) user.Email = existingUser.Email;

        await _userRepository.UpdateAsync(user);
        await _cacheRepository.RemoveAsync($"user:{id}");
        await _cacheRepository.RemoveAsync("users:all");
        await _cacheRepository.SetAsync($"user:{id}", user, TimeSpan.FromMinutes(10));

        // Publish user.updated event (best effort)
        if (_messagePublisher != null)
        {
            await _messagePublisher.PublishUserUpdatedAsync(user);
        }

        _logger.LogInformation("User updated successfully: {UserId}", id);
    }

    public async Task ProcessUserCreatedAsync(string userId)
    {
        await _singleFlight.DoAsync($"process_user_created:{userId}", async () =>
        {
            _logger.LogInformation("Processing user.created event for user: {UserId}", userId);

            User? user = null;
            bool useGrpc = false;

            // Check feature flag to determine which repository to use
            if (_featureFlagService != null)
            {
                var context = SkeletonApi.Common.FeatureFlags.FeatureFlagExtensions.CreateContext(userId);
                useGrpc = await _featureFlagService.IsEnabledAsync("grpc-client", false, context);
            }

            if (useGrpc)
            {
                // Use gRPC repository
                if (_grpcRepository != null)
                {
                    _logger.LogInformation("Using gRPC client (feature flag enabled)");
                    user = await _grpcRepository.GetUserAsync(userId);

                    if (user != null)
                    {
                        _logger.LogInformation("Successfully retrieved user via gRPC: {UserId}, {Username}, {Email}",
                            user.Id, user.Username, user.Email);
                    }
                    else
                    {
                        _logger.LogError("Failed to get user via gRPC: {UserId}", userId);
                    }
                }
                else
                {
                    _logger.LogWarning("gRPC repository not initialized, falling back to local repository");
                    user = await GetByIdAsync(userId);
                }
            }
            else
            {
                // Use REST repository
                if (_restRepository != null)
                {
                    _logger.LogInformation("Using REST client (feature flag disabled)");
                    user = await _restRepository.GetUserAsync(userId);

                    if (user != null)
                    {
                        _logger.LogInformation("Successfully retrieved user via REST: {UserId}, {Username}, {Email}",
                            user.Id, user.Username, user.Email);
                    }
                    else
                    {
                        _logger.LogError("Failed to get user via REST: {UserId}", userId);
                    }
                }
                else
                {
                    _logger.LogWarning("REST repository not initialized, falling back to local repository");
                    user = await GetByIdAsync(userId);
                }
            }

            if (user != null)
            {
                // Publish to SSE
                if (_sseManager != null)
                {
                    await _sseManager.PublishToRedisAsync(new SseEvent
                    {
                        Type = "user_created",
                        Data = user
                    });
                    _logger.LogInformation("Published user_created event to SSE for user: {UserId}", userId);
                }
            }
            return 0; // Dummy return for Task<T>
        });
    }
}
