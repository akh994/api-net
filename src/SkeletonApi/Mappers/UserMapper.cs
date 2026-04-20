using Google.Protobuf.WellKnownTypes;
using SkeletonApi.Domain.Entities;
using SkeletonApi.Protos;

namespace SkeletonApi.Mappers;

public static class UserMapper
{
    public static UserModel ToProto(this User user)
    {
        return new UserModel
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Password = user.Password,
            FullName = user.FullName,
            Role = user.Role,
            IsActive = user.IsActive,
            EmailVerified = user.EmailVerified,
            CreatedAt = Timestamp.FromDateTime(user.CreatedAt.ToUniversalTime()),
            UpdatedAt = Timestamp.FromDateTime(user.UpdatedAt.ToUniversalTime()),
            Avatar = user.Avatar ?? string.Empty
        };
    }

    public static User ToDomain(this UserModel proto)
    {
        return new User
        {
            Id = proto.Id,
            Username = proto.Username,
            Email = proto.Email,
            Password = proto.Password,
            FullName = proto.FullName,
            Role = proto.Role,
            IsActive = proto.IsActive ?? true,
            EmailVerified = proto.EmailVerified ?? false,
            Avatar = string.IsNullOrEmpty(proto.Avatar) ? null : proto.Avatar
        };
    }
}
