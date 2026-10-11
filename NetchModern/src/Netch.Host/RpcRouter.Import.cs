using Netch.Core.Models.Servers;
using Netch.Core.Services;
using Netch.Core.Services.Import;

namespace Netch.Host;

// servers.import implementation (split out of RpcRouter.cs).
internal sealed partial class RpcRouter
{
    /// <summary>
    ///     Implements servers.import: a single http(s) URL becomes a subscription (created like subs.add,
    ///     then refreshed); anything else is parsed by NodeImporter and deduped against the current pool.
    /// </summary>
    private async Task<object> ImportNodesAsync(string text, CancellationToken ct)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("无法识别的节点内容");

        // A single http(s) URL → treat as a subscription: create it exactly like subs.add, then refresh.
        if (IsSingleHttpUrl(trimmed))
        {
            // Under the state lock: find an already configured subscription with the same link and
            // snapshot the current pool size (used to report how many servers the refresh added).
            var (existing, poolCountBefore) = await WithStateValue(() =>
            {
                var found = Subs.Subscriptions.FirstOrDefault(s =>
                    string.Equals(s.Link?.Trim() ?? "", trimmed, StringComparison.Ordinal));
                return (found, Servers.Servers.ToList().Count);
            }, ct);

            if (existing != null)
            {
                // Duplicate link: do not create a second subscription, just refresh the existing one.
                await RefreshSubscriptionAsync(existing.Id, ct);
            }
            else
            {
                var sub = await WithStateValueAsync(async () =>
                {
                    var s = new Netch.Core.Models.Subscription
                    {
                        Remark = Netch.Core.Models.Subscription.DefaultRemarkFor(trimmed),
                        Link = trimmed
                    };
                    s.EnsureId();
                    Subs.Add(s);
                    await Persistence.SaveAsync();
                    return s;
                }, ct);

                try
                {
                    await RefreshSubscriptionAsync(sub.Id, ct);
                }
                catch (Exception)
                {
                    // First refresh of a newly added subscription failed: don't leave an orphan
                    // (empty) subscription behind. Remove it again if it is still present.
                    await WithStateValueAsync(async () =>
                    {
                        var s = Subs.Subscriptions.FirstOrDefault(x => x.Id == sub.Id);
                        if (s != null)
                        {
                            Subs.Remove(s);
                            await Persistence.SaveAsync();
                        }
                        return 0;
                    }, CancellationToken.None); // cleanup must run even when the import was cancelled
                    throw;
                }
            }

            return await WithStateValue(() =>
            {
                // Report only the servers the refresh added to the pool, not the whole pool.
                var added = Math.Max(0, Servers.Servers.Count - poolCountBefore);
                var list = ServerList();
                return (object)new ImportResultDto(added, 0, 0, "subscription", list);
            }, ct);
        }

        var parsed = NodeImporter.Parse(trimmed);

        return await WithStateValueAsync(async () =>
        {
            var (added, duplicates) = NodeImporter.Dedupe(Servers.Servers.ToList(), parsed.Valid);
            if (added.Count == 0 && duplicates == 0)
                throw new InvalidOperationException("无法识别的节点内容");

            foreach (var s in added)
                Servers.Add(s);
            if (added.Count > 0)
                await Persistence.SaveAsync();

            var list = ServerList();
            return (object)new ImportResultDto(added.Count, duplicates, parsed.Invalid, parsed.Format, list);
        }, ct);
    }

    /// <summary>True when the whole (trimmed) text is a single http:// or https:// URL.</summary>
    private static bool IsSingleHttpUrl(string text)
    {
        if (text.Contains('\n') || text.Contains('\r'))
            return false;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }
}
