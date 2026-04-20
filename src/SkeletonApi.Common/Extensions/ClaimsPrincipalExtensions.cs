using System.Security.Claims;

namespace SkeletonApi.Common.Extensions;

/// <summary>
/// Extensions for ClaimsPrincipal to easily access common claims
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst("id")?.Value
               ?? string.Empty;
    }

    public static string GetUsername(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Name)?.Value
               ?? principal.FindFirst("username")?.Value
               ?? string.Empty;
    }

    public static string GetEmail(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Email)?.Value
               ?? principal.FindFirst("email")?.Value
               ?? string.Empty;
    }

    public static string GetRole(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Role)?.Value
               ?? principal.FindFirst("role")?.Value
               ?? string.Empty;
    }
}
