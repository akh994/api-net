using Grpc.Core;
using SkeletonApi.Application.Interfaces;
using SkeletonApi.Mappers;
using SkeletonApi.Protos;

namespace SkeletonApi.Services;

public class UserGrpcService : Protos.UserGrpcService.UserGrpcServiceBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UserGrpcService> _logger;

    public UserGrpcService(IUserService userService, ILogger<UserGrpcService> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    public override async Task<ResUserMessage> Add(UserModel request, ServerCallContext context)
    {
        var user = request.ToDomain();
        await _userService.CreateAsync(user);
        return new ResUserMessage { Message = "User created successfully" };
    }

    public override async Task<ResUserAll> GetAll(UserEmpty request, ServerCallContext context)
    {
        var users = await _userService.GetAllAsync();
        var response = new ResUserAll();
        response.Items.AddRange(users.Select(u => u.ToProto()));
        return response;
    }

    public override async Task<UserModel> GetById(UserByIdRequest request, ServerCallContext context)
    {
        var user = await _userService.GetByIdAsync(request.Id);
        if (user == null)
        {
            throw new SkeletonApi.Common.Errors.NotFoundException($"User with ID {request.Id} not found");
        }
        return user.ToProto();
    }

    public override async Task<ResUserMessage> Update(UserModel request, ServerCallContext context)
    {
        var user = request.ToDomain();
        await _userService.UpdateAsync(request.Id, user);
        return new ResUserMessage { Message = "User updated successfully" };
    }

    public override async Task<ResUserMessage> Delete(UserByIdRequest request, ServerCallContext context)
    {
        await _userService.DeleteAsync(request.Id);
        return new ResUserMessage { Message = "User deleted successfully" };
    }

    public override async Task<ResUserAll> Search(UserSearchRequest request, ServerCallContext context)
    {
        var users = await _userService.SearchAsync(request.Query);
        var response = new ResUserAll();
        response.Items.AddRange(users.Select(u => u.ToProto()));
        return response;
    }

    public override async Task<ResUserPaginated> GetAllPaginated(PaginationRequest request, ServerCallContext context)
    {
        // Set defaults if not provided
        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 10;

        var (items, totalItems) = await _userService.GetAllPaginatedAsync(page, pageSize);

        var response = new ResUserPaginated
        {
            Pagination = new PaginationMeta
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalItems / pageSize) : 0
            }
        };
        response.Items.AddRange(items.Select(u => u.ToProto()));

        return response;
    }
}
