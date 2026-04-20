using System.Text.Json;

namespace SkeletonApi.Common.Models;

/// <summary>
/// Represents user identity claims extracted from JWT or headers
/// Aligned with Go skeleton-api-go Claims struct
/// </summary>
public class UserClaims
{
    /// <summary>
    /// User ID (from "sub" claim or x-user-id header)
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// User UUID (from "user_uuid" claim or x-user-uuid header)
    /// </summary>
    public Guid? UserUuid { get; set; }

    /// <summary>
    /// Domain ID (from "domain_id" claim or x-domain-id header)
    /// </summary>
    public Guid? DomainId { get; set; }

    /// <summary>
    /// Domain Name (from "domain_name" claim or x-domain-name header)
    /// </summary>
    public string DomainName { get; set; } = string.Empty;

    /// <summary>
    /// Domain Type (from "domain_type" claim or x-domain-type header)
    /// </summary>
    public string DomainType { get; set; } = string.Empty;

    /// <summary>
    /// Group Name (from "group_name" claim or x-group-name header)
    /// </summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// Email (from "email" claim or x-user-email header)
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Phone Number (from "phone_number" claim or x-phone-number header)
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Role (from "role" claim or x-user-role header)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Additional unmapped claims from JWT token
    /// Supports complex types including nested objects and arrays
    /// </summary>
    public Dictionary<string, object>? ExtraAttributes { get; set; }

    /// <summary>
    /// Serialize ExtraAttributes to JSON string for propagation via headers/metadata
    /// </summary>
    public string? SerializeExtraAttributes()
    {
        if (ExtraAttributes == null || ExtraAttributes.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(ExtraAttributes);
    }

    /// <summary>
    /// Deserialize ExtraAttributes from JSON string
    /// </summary>
    public static Dictionary<string, object>? DeserializeExtraAttributes(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    // Backward compatibility properties (use new names in new code)
    public string Id
    {
        get => UserId;
        set => UserId = value;
    }

    public string Username
    {
        get => UserId;
        set => UserId = value;
    }
}
