using System.Text.Json;

namespace SkeletonApi.Common.Configuration;

public static class ConfigHelper
{
    /// <summary>
    /// Merges two configuration dictionaries. Specific config overrides general config.
    /// </summary>
    public static Dictionary<string, object> Merge(Dictionary<string, object> general, Dictionary<string, object> specific)
    {
        var result = new Dictionary<string, object>(general ?? new Dictionary<string, object>());

        if (specific != null)
        {
            foreach (var kvp in specific)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Deserializes a dictionary configuration into a strongly typed object.
    /// </summary>
    public static T? DecodeConfig<T>(Dictionary<string, object> config)
    {
        if (config == null || config.Count == 0)
            return default;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var json = JsonSerializer.Serialize(config);
        return JsonSerializer.Deserialize<T>(json, options);
    }

    /// <summary>
    /// Converts an object (likely from configuration binding) to a Dictionary.
    /// </summary>
    public static Dictionary<string, object> ConvertToDictionary(object? obj)
    {
        if (obj == null) return new Dictionary<string, object>();
        if (obj is Dictionary<string, object> dict) return dict;

        // Try to deserialize if it's a JsonElement (common in .NET 6+ configuration binding to object)
        if (obj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            var json = jsonElement.GetRawText();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json, options) ?? new Dictionary<string, object>();
        }

        // Fallback: Serialize and deserialize
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var json = JsonSerializer.Serialize(obj);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json, options) ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }
}

public class BooleanConverter : System.Text.Json.Serialization.JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True) return true;
        if (reader.TokenType == JsonTokenType.False) return false;
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (bool.TryParse(value, out var result)) return result;
            if (value == "1") return true;
            if (value == "0") return false;
        }
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt32() != 0;
        }
        return false;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}
