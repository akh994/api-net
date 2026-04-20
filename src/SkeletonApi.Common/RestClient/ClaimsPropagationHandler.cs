using System.Net.Http.Headers;
using SkeletonApi.Common.Interfaces;

namespace SkeletonApi.Common.RestClient;

/// <summary>
/// DelegatingHandler to propagate user claims to downstream REST services
/// </summary>
public class ClaimsPropagationHandler : DelegatingHandler
{
    private readonly IUserContext _userContext;

    public ClaimsPropagationHandler(IUserContext userContext)
    {
        _userContext = userContext;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var claims = _userContext.GetCurrentUser();

        // Propagate standard claims
        if (!string.IsNullOrEmpty(claims.UserId))
        {
            request.Headers.TryAddWithoutValidation("x-user-id", claims.UserId);
        }

        if (claims.UserUuid.HasValue)
        {
            request.Headers.TryAddWithoutValidation("x-user-uuid", claims.UserUuid.Value.ToString());
        }

        if (claims.DomainId.HasValue)
        {
            request.Headers.TryAddWithoutValidation("x-domain-id", claims.DomainId.Value.ToString());
        }

        if (!string.IsNullOrEmpty(claims.DomainName))
        {
            request.Headers.TryAddWithoutValidation("x-domain-name", claims.DomainName);
        }

        if (!string.IsNullOrEmpty(claims.DomainType))
        {
            request.Headers.TryAddWithoutValidation("x-domain-type", claims.DomainType);
        }

        if (!string.IsNullOrEmpty(claims.GroupName))
        {
            request.Headers.TryAddWithoutValidation("x-group-name", claims.GroupName);
        }

        if (!string.IsNullOrEmpty(claims.Email))
        {
            request.Headers.TryAddWithoutValidation("x-user-email", claims.Email);
        }

        if (!string.IsNullOrEmpty(claims.PhoneNumber))
        {
            request.Headers.TryAddWithoutValidation("x-phone-number", claims.PhoneNumber);
        }

        if (!string.IsNullOrEmpty(claims.Role))
        {
            request.Headers.TryAddWithoutValidation("x-user-role", claims.Role);
        }

        // Propagate extra attributes as JSON
        var extraAttributesJson = claims.SerializeExtraAttributes();
        if (!string.IsNullOrEmpty(extraAttributesJson))
        {
            request.Headers.TryAddWithoutValidation("x-extra-attributes", extraAttributesJson);
        }

        // Backward compatibility - also send x-user-name
        if (!string.IsNullOrEmpty(claims.UserId))
        {
            request.Headers.TryAddWithoutValidation("x-user-name", claims.UserId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
