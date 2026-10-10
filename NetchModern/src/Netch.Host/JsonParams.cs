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

    /// <summary>
    ///     Returns an optional integer field, or <c>null</c> if the property is absent. Throws when the
    ///     property is present but not an integer (a non-number, a fractional number or an out-of-range
    ///     value) so a bad client payload can't be silently coerced to 0.
    /// </summary>
    public static int? OptionalInt(in JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v))
            return null;

        if (v.ValueKind != JsonValueKind.Number)
            throw new InvalidOperationException($"{name} 必须是整数");

        if (!v.TryGetInt32(out var value))
            throw new InvalidOperationException($"{name} 必须是整数");

        return value;
    }

    /// <summary>True if the property is present (any JSON kind).</summary>
    public static bool HasProperty(in JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out _);

    /// <summary>Returns an array-of-strings field, or an empty list if absent / not a JSON array.</summary>
    public static List<string> StringList(in JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return new();
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString() ?? "");
        return list;
    }
}
