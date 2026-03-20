using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlackScreenIdentifier.Core.Utilities;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
