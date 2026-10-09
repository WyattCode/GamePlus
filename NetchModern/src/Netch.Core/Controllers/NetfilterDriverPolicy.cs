using System.Security.Cryptography;
using System.Text.Json;

namespace Netch.Core.Controllers;

/// <summary>What <c>RedirectorModeController</c> should do with the installed netfilter2 driver.</summary>
public enum NetfilterDriverAction
{
    /// <summary>Driver not installed at all — copy ours and register the service.</summary>
    Install,

    /// <summary>Installed driver is (equivalent to) ours — just make sure the service is registered.</summary>
    UseExisting,

    /// <summary>Installed driver is an older copy WE wrote earlier — replace it with the bundled one.</summary>
    Replace,

    /// <summary>
    ///     Installed driver belongs to another program (same NetFilter SDK, same service name). Leave the
    ///     file and the service completely alone and try to use it as-is.
    /// </summary>
    UseForeign
}

/// <summary>
///     Pure decision logic for the netfilter2 driver install/upgrade path, extracted so it can be unit
///     tested without touching Windows APIs or the file system.
///     <para>
///         Several Chinese game accelerators / proxy tools ship the same NetFilter SDK driver under the
///         same "netfilter2" service name. We must never unregister, delete or downgrade a driver we did
///         not write ourselves — doing so breaks the other program. Ownership is tracked by recording the
///         SHA-256 of the file we installed in <c>netfilter2-owner.json</c>.
///     </para>
/// </summary>
public static class NetfilterDriverPolicy
{
    /// <summary>
    ///     Decides what to do with the installed driver.
    /// </summary>
    /// <param name="installedExists">Whether <c>System32\drivers\netfilter2.sys</c> exists.</param>
    /// <param name="installedSha256">SHA-256 (lower-case hex) of the installed file, or null if unknown.</param>
    /// <param name="installedVersion">FileVersion of the installed file (may be empty).</param>
    /// <param name="bundledSha256">SHA-256 (lower-case hex) of the bundled <c>nfdriver.sys</c>.</param>
    /// <param name="bundledVersion">FileVersion of the bundled <c>nfdriver.sys</c> (may be empty).</param>
    /// <param name="ownedSha256">
    ///     SHA-256 recorded in our ownership marker when we last installed the driver, or null if we have
    ///     no marker (never installed by us / marker missing or corrupt).
    /// </param>
    public static NetfilterDriverAction Decide(
        bool installedExists,
        string? installedSha256,
        string installedVersion,
        string bundledSha256,
        string bundledVersion,
        string? ownedSha256)
    {
        if (!installedExists)
            return NetfilterDriverAction.Install;

        // Same bytes as ours → nothing to do.
        if (!string.IsNullOrEmpty(installedSha256) &&
            !string.IsNullOrEmpty(bundledSha256) &&
            string.Equals(installedSha256, bundledSha256, StringComparison.OrdinalIgnoreCase))
            return NetfilterDriverAction.UseExisting;

        // Same (non-empty) version → treat as equivalent even if the hash differs (re-signed builds etc.).
        if (!string.IsNullOrEmpty(installedVersion) &&
            !string.IsNullOrEmpty(bundledVersion) &&
            string.Equals(installedVersion, bundledVersion, StringComparison.OrdinalIgnoreCase))
            return NetfilterDriverAction.UseExisting;

        // Differs, but it is the file WE wrote earlier (older Game+ build) → safe to replace.
        if (!string.IsNullOrEmpty(installedSha256) &&
            !string.IsNullOrEmpty(ownedSha256) &&
            string.Equals(installedSha256, ownedSha256, StringComparison.OrdinalIgnoreCase))
            return NetfilterDriverAction.Replace;

        // Someone else's driver — hands off.
        return NetfilterDriverAction.UseForeign;
    }

    /// <summary>Path of the ownership marker file.</summary>
    internal static string OwnerMarkerPath => Path.Combine(AppPaths.DataDirectory, "netfilter2-owner.json");

    /// <summary>
    ///     Reads the SHA-256 we recorded when we last installed the driver. Tolerant: missing, unreadable
    ///     or corrupt marker → null.
    /// </summary>
    internal static string? ReadOwnedSha256()
    {
        try
        {
            var path = OwnerMarkerPath;
            if (!File.Exists(path))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!doc.RootElement.TryGetProperty("sha256", out var sha) || sha.ValueKind != JsonValueKind.String)
                return null;

            var value = sha.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Records the SHA-256 of the driver file we just wrote. Written atomically (temp file + move) so a
    ///     crash cannot leave a half-written marker behind. Best-effort: failures are swallowed.
    /// </summary>
    internal static void WriteOwnedSha256(string sha256, string version)
    {
        try
        {
            var path = OwnerMarkerPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var json = JsonSerializer.Serialize(new
            {
                sha256,
                version,
                installedAt = DateTimeOffset.UtcNow.ToString("O")
            });

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, true);
        }
        catch
        {
            // Best-effort only: without a marker we simply treat the file as foreign next time.
        }
    }

    /// <summary>SHA-256 (lower-case hex) of a file, or null if it cannot be read.</summary>
    internal static string? Sha256OfFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
