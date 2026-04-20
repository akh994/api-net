namespace SkeletonApi.Common.Utilities;

/// <summary>
/// Utility class for generating unique identifiers
/// </summary>
public static class IdGenerator
{
    /// <summary>
    /// Generates a new GUID as string
    /// </summary>
    public static string NewId()
    {
        return Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Generates a new GUID without hyphens
    /// </summary>
    public static string NewShortId()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Generates a sortable ID using timestamp + GUID
    /// </summary>
    public static string NewSortableId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = Guid.NewGuid().ToString("N").Substring(0, 16);
        return $"{timestamp:x16}{guid}";
    }

    /// <summary>
    /// Generates a random alphanumeric string
    /// </summary>
    public static string NewRandomString(int length = 8)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}
