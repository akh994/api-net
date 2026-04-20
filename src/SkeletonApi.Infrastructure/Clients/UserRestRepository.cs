using Microsoft.Extensions.Logging;
using SkeletonApi.Application.Interfaces;
using SkeletonApi.Common.Errors;
using SkeletonApi.Common.Http.Response;
using SkeletonApi.Common.RestClient;
using SkeletonApi.Domain.Entities;

namespace SkeletonApi.Infrastructure.Clients;

public class UserRestRepository : IUserRestRepository
{
    private readonly RestClientRepository _clientRepository;
    private readonly ILogger<UserRestRepository> _logger;

    public UserRestRepository(
        RestClientRepository clientRepository,
        ILogger<UserRestRepository> logger)
    {
        _clientRepository = clientRepository;
        _logger = logger;
    }

    public async Task<User?> GetUserAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            // Correct endpoint is /v1/user/{id}
            var response = await _clientRepository.GetAsync<ApiResponse<User>>($"/v1/user/{id}", cancellationToken);

            return response?.Data;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new DataAccessHubException($"Failed to get user {id} via REST", ex);
        }
    }
}
