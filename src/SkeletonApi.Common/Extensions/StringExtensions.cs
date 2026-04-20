namespace SkeletonApi.Common.Extensions;

/// <summary>
/// Extension methods for string manipulation
/// </summary>
public static class StringExtensions
{
    public static bool IsNullOrEmpty(this string? value)
    {
        return string.IsNullOrEmpty(value);
    }

    public static bool IsNullOrWhiteSpace(this string? value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    public static string ToCamelCase(this string value)
    {
        if (value.IsNullOrEmpty())
            return value;

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    public static string ToPascalCase(this string value)
    {
        if (value.IsNullOrEmpty())
            return value;

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    public static string ToSnakeCase(this string value)
    {
        if (value.IsNullOrEmpty())
            return value;

        return string.Concat(
            value.Select((x, i) => i > 0 && char.IsUpper(x)
                ? "_" + x.ToString()
                : x.ToString())).ToLower();
    }

    public static string Truncate(this string value, int maxLength)
    {
        if (value.IsNullOrEmpty() || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength);
    }

    public static string? NullIfEmpty(this string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
