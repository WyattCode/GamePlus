using System.Text;

namespace Netch.Core.Services.Library;

/// <summary>
///     A parsed Valve KeyValues node: a case-insensitive map of key → string or nested <see cref="VdfNode" />.
/// </summary>
public sealed class VdfNode
{
    private readonly Dictionary<string, object> _values =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys in insertion order.</summary>
    public IReadOnlyCollection<string> Keys => _values.Keys;

    /// <summary>Number of entries.</summary>
    public int Count => _values.Count;

    /// <summary>Returns the raw value (string or <see cref="VdfNode" />) for <paramref name="key" />, or null.</summary>
    public object? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>Returns the string value for <paramref name="key" />, or null if absent / not a string.</summary>
    public string? GetString(string key) => Get(key) as string;

    /// <summary>Returns the nested node for <paramref name="key" />, or null if absent / not a node.</summary>
    public VdfNode? GetNode(string key) => Get(key) as VdfNode;

    /// <summary>Enumerates the nested nodes (skipping string values).</summary>
    public IEnumerable<VdfNode> Nodes()
    {
        foreach (var v in _values.Values)
            if (v is VdfNode n)
                yield return n;
    }

    /// <summary>Enumerates the (key, node) pairs whose value is a nested node.</summary>
    public IEnumerable<KeyValuePair<string, VdfNode>> NodeEntries()
    {
        foreach (var kv in _values)
            if (kv.Value is VdfNode n)
                yield return new KeyValuePair<string, VdfNode>(kv.Key, n);
    }

    internal void Set(string key, object value) => _values[key] = value;
}

/// <summary>
///     Minimal Valve KeyValues ("VDF") text parser. Supports quoted keys/values, nested <c>{ }</c> objects,
///     <c>//</c> line comments and the <c>\"</c> / <c>\\</c> escapes. Unquoted tokens are also accepted.
/// </summary>
public static class VdfParser
{
    /// <summary>Parses <paramref name="text" /> into a root <see cref="VdfNode" />. Never throws for malformed input.</summary>
    public static VdfNode Parse(string text)
    {
        var root = new VdfNode();
        if (string.IsNullOrEmpty(text))
            return root;

        var i = 0;
        ParseObject(text, ref i, root, topLevel: true);
        return root;
    }

    private static void ParseObject(string s, ref int i, VdfNode node, bool topLevel)
    {
        while (i < s.Length)
        {
            SkipWhitespaceAndComments(s, ref i);
            if (i >= s.Length)
                return;

            if (s[i] == '}')
            {
                i++;
                if (!topLevel)
                    return;
                continue;
            }

            var key = ReadToken(s, ref i);
            if (key is null)
                return;

            SkipWhitespaceAndComments(s, ref i);
            if (i >= s.Length)
                return;

            if (s[i] == '{')
            {
                i++;
                var child = new VdfNode();
                ParseObject(s, ref i, child, topLevel: false);
                node.Set(key, child);
                continue;
            }

            var value = ReadToken(s, ref i);
            if (value is null)
                return;
            node.Set(key, value);
        }
    }

    private static void SkipWhitespaceAndComments(string s, ref int i)
    {
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                i += 2;
                while (i < s.Length && s[i] != '\n')
                    i++;
                continue;
            }

            break;
        }
    }

    /// <summary>Reads a quoted or bare token. Returns null at end of input.</summary>
    private static string? ReadToken(string s, ref int i)
    {
        SkipWhitespaceAndComments(s, ref i);
        if (i >= s.Length)
            return null;

        if (s[i] == '"')
        {
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    var next = s[i + 1];
                    if (next == '"' || next == '\\')
                    {
                        sb.Append(next);
                        i += 2;
                        continue;
                    }
                }

                if (c == '"')
                {
                    i++;
                    break;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        // Bare token: read until whitespace or a structural character.
        var start = i;
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"')
            i++;
        if (i == start)
        {
            i++; // skip the unexpected character so we can't loop forever
            return null;
        }

        return s.Substring(start, i - start);
    }
}
