using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SkeletonApi.Common.Interfaces;
using SkeletonApi.Common.Models;

namespace SkeletonApi.Common.Services;

/// <summary>
/// Implementation of IUserContext using IHttpContextAccessor
/// </summary>
public class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private UserClaims? _manualClaims;

    public UserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public UserClaims GetCurrentUser()
    {
        if (_manualClaims != null)
        {
            return _manualClaims;
        }

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || user.Identity?.IsAuthenticated != true)
        {
            // Return empty/anonymous claims
            return new UserClaims();
        }

        var claims = new UserClaims
        {
            UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst("user_id")?.Value
                     ?? user.FindFirst("id")?.Value
                     ?? string.Empty,
            Email = user.FindFirst(ClaimTypes.Email)?.Value
                    ?? user.FindFirst("email")?.Value
                    ?? string.Empty,
            Role = user.FindFirst(ClaimTypes.Role)?.Value
                   ?? user.FindFirst("role")?.Value
                   ?? string.Empty
        };

        // Extract UserUuid
        var userUuidStr = user.FindFirst("user_uuid")?.Value;
        if (!string.IsNullOrEmpty(userUuidStr) && Guid.TryParse(userUuidStr, out var userUuid))
        {
            claims.UserUuid = userUuid;
        }

        // Extract DomainId
        var domainIdStr = user.FindFirst("domain_id")?.Value;
        if (!string.IsNullOrEmpty(domainIdStr) && Guid.TryParse(domainIdStr, out var domainId))
        {
            claims.DomainId = domainId;
        }

        // Extract other fields
        claims.DomainName = user.FindFirst("domain_name")?.Value ?? string.Empty;
        claims.DomainType = user.FindFirst("domain_type")?.Value ?? string.Empty;
        claims.GroupName = user.FindFirst("group_name")?.Value ?? string.Empty;
        claims.PhoneNumber = user.FindFirst("phone_number")?.Value ?? string.Empty;

        // Extract ExtraAttributes from JSON
        var extraAttributesJson = user.FindFirst("extra_attributes")?.Value;
        if (!string.IsNullOrEmpty(extraAttributesJson))
        {
            claims.ExtraAttributes = UserClaims.DeserializeExtraAttributes(extraAttributesJson);
        }

        return claims;
    }

    public string GetTraceId()
    {
        return _httpContextAccessor.HttpContext?.TraceIdentifier
               ?? System.Diagnostics.Activity.Current?.Id
               ?? string.Empty;
    }

    public bool IsAuthenticated()
    {
        if (_manualClaims != null)
        {
            return !string.IsNullOrEmpty(_manualClaims.UserId);
        }

        return _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
    }

    public void SetUser(UserClaims claims)
    {
        _manualClaims = claims;
    }
}
