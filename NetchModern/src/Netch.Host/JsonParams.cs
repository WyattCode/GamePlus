using System.Text.Json;

namespace Netch.Host;

/// <summary>
///     Small helpers for reading named fields off a JSON-RPC <c>params</c> element (a flat JSON object).
///     Centralises the repeated TryGetProperty / ValueKind / GetString boilerplate in <see cref="RpcRouter" />.
/// </summary>
internal static class JsonParams
{
    /// <summary>Returns a required string field. Throws if the property is absent (non-string is treated as "").</summary>
    public static string RequiredString(in JsonElement obj, string name) =>
        obj.GetProperty(name).GetString() ?? "";

    /// <summary>Returns an optional string field, or <c>null</c> if the property is absent or not a string.</summary>
    public static string? OptionalString(in JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Returns an optional boolean field, or <c>null</c> if the property is absent or not a JSON bool.</summary>
    public static bool? OptionalBool(in JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
