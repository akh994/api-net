using Grpc.Core;
using Microsoft.Extensions.Logging;
using SkeletonApi.Application.Interfaces;
using SkeletonApi.Common.Errors;
using SkeletonApi.Common.GrpcClient;
using SkeletonApi.Domain.Entities;
using SkeletonApi.Protos;

namespace SkeletonApi.Infrastructure.Clients;

public class UserGrpcRepository : IUserGrpcRepository
{
    private readonly GrpcClientRepository<UserGrpcService.UserGrpcServiceClient> _clientRepository;
    private readonly ILogger<UserGrpcRepository> _logger;

    public UserGrpcRepository(
        GrpcClientRepository<UserGrpcService.UserGrpcServiceClient> clientRepository,
        ILogger<UserGrpcRepository> logger)
    {
        _clientRepository = clientRepository;
        _logger = logger;
    }

    public async Task<User?> GetUserAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new UserByIdRequest { Id = id };
            var response = await _clientRepository.CallAsync(
                (client, req) => client.GetByIdAsync(req).ResponseAsync,
                request,
                cancellationToken);

            if (response == null) return null;

            return new User
            {
                Id = response.Id,
                Username = response.Username,
                Email = response.Email,
                FullName = response.FullName,
                Role = response.Role,
                CreatedAt = response.CreatedAt.ToDateTime(),
                UpdatedAt = response.UpdatedAt.ToDateTime()
            };
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new DataAccessHubException($"Failed to get user {id} via gRPC", ex);
        }
    }
}
