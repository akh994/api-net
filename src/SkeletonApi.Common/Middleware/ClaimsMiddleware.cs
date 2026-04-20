using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SkeletonApi.Common.Models;

namespace SkeletonApi.Common.Middleware;

/// <summary>
/// Middleware to extract user claims from request headers
/// and populate the User context if not already authenticated.
/// This is useful when behind an API Gateway that handles authentication.
/// </summary>
public class ClaimsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ClaimsMiddleware> _logger;

    public ClaimsMiddleware(RequestDelegate next, ILogger<ClaimsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // If user is already authenticated (e.g. by JWT bearer token), skip header extraction
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        var userId = context.Request.Headers["x-user-id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(userId))
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("user_id", userId)
            };

            // Extract standard claims
            var userUuid = context.Request.Headers["x-user-uuid"].FirstOrDefault();
            if (!string.IsNullOrEmpty(userUuid))
            {
                claims.Add(new Claim("user_uuid", userUuid));
            }

            var domainId = context.Request.Headers["x-domain-id"].FirstOrDefault();
            if (!string.IsNullOrEmpty(domainId))
            {
                claims.Add(new Claim("domain_id", domainId));
            }

            var domainName = context.Request.Headers["x-domain-name"].FirstOrDefault();
            if (!string.IsNullOrEmpty(domainName))
            {
                claims.Add(new Claim("domain_name", domainName));
            }

            var domainType = context.Request.Headers["x-domain-type"].FirstOrDefault();
            if (!string.IsNullOrEmpty(domainType))
            {
                claims.Add(new Claim("domain_type", domainType));
            }

            var groupName = context.Request.Headers["x-group-name"].FirstOrDefault();
            if (!string.IsNullOrEmpty(groupName))
            {
                claims.Add(new Claim("group_name", groupName));
            }

            var email = context.Request.Headers["x-user-email"].FirstOrDefault();
            if (!string.IsNullOrEmpty(email))
            {
                claims.Add(new Claim(ClaimTypes.Email, email));
                claims.Add(new Claim("email", email));
            }

            var phoneNumber = context.Request.Headers["x-phone-number"].FirstOrDefault();
            if (!string.IsNullOrEmpty(phoneNumber))
            {
                claims.Add(new Claim("phone_number", phoneNumber));
            }

            var role = context.Request.Headers["x-user-role"].FirstOrDefault();
            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
                claims.Add(new Claim("role", role));
            }

            // Extract extra attributes from JSON header
            var extraAttributesJson = context.Request.Headers["x-extra-attributes"].FirstOrDefault();
            if (!string.IsNullOrEmpty(extraAttributesJson))
            {
                claims.Add(new Claim("extra_attributes", extraAttributesJson));
            }

            // Backward compatibility - also check x-user-name
            var username = context.Request.Headers["x-user-name"].FirstOrDefault();
            if (!string.IsNullOrEmpty(username))
            {
                claims.Add(new Claim(ClaimTypes.Name, username));
            }

            // Create identity and user
            var identity = new ClaimsIdentity(claims, "Gateway");
            context.User = new ClaimsPrincipal(identity);

            _logger.LogDebug("MiddleWare: Populated UserPrincipal from headers. UserId: {UserId}, Role: {Role}", userId, role);
        }

        await _next(context);
    }
}

/// <summary>
/// Extension method to register ClaimsMiddleware
/// </summary>
public static class ClaimsMiddlewareExtensions
{
    public static Microsoft.AspNetCore.Builder.IApplicationBuilder UseClaimsMiddleware(this Microsoft.AspNetCore.Builder.IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ClaimsMiddleware>();
    }
}
