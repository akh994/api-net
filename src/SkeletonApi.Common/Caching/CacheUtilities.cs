namespace SkeletonApi.Common.Caching;

/// <summary>
/// Cache key builder for consistent cache key generation
/// </summary>
public static class CacheKeyBuilder
{
    private const string Separator = ":";

    public static string Build(params string[] parts)
    {
        return string.Join(Separator, parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    public static string BuildForEntity<T>(string id)
    {
        return Build(typeof(T).Name.ToLower(), id);
    }

    public static string BuildForList<T>(string? filter = null)
    {
        var parts = new List<string> { typeof(T).Name.ToLower(), "list" };
        if (!string.IsNullOrEmpty(filter))
        {
            parts.Add(filter);
        }
        return Build(parts.ToArray());
    }

    public static string BuildForSearch<T>(string query)
    {
        return Build(typeof(T).Name.ToLower(), "search", query.ToLower());
    }

    public static string BuildForPaginated<T>(int page, int pageSize)
    {
        return Build(typeof(T).Name.ToLower(), "paginated", $"p{page}", $"s{pageSize}");
    }
}

/// <summary>
/// Standard cache expiration times
/// </summary>
public static class CacheExpiration
{
    public static TimeSpan Short => TimeSpan.FromMinutes(5);
    public static TimeSpan Medium => TimeSpan.FromMinutes(15);
    public static TimeSpan Long => TimeSpan.FromHours(1);
    public static TimeSpan VeryLong => TimeSpan.FromHours(24);

    public static TimeSpan Custom(int minutes) => TimeSpan.FromMinutes(minutes);
}
