using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Sockets;
using Netch.Core.Abstractions;
using Netch.Core.Models;
using Netch.Core.Models.Modes;
using Netch.Core.Models.Servers;

namespace Netch.Core.Services;

public sealed class ServerRepository : IServerRepository
{
    public ServerRepository()
    {
        foreach (var s in SampleData.CreateServers())
            Servers.Add(s);
    }

    public ObservableCollection<Server> Servers { get; } = new();

    public void Add(Server server) => Servers.Add(server);

    public void Remove(Server server) => Servers.Remove(server);

    public void Replace(Server existing, Server replacement)
    {
        var index = Servers.IndexOf(existing);
        if (index >= 0)
            Servers[index] = replacement;
        else
            Servers.Add(replacement);
    }

    public async Task PingAllAsync(CancellationToken ct = default)
    {
        foreach (var server in Servers)
            server.Delay = -1;

        // Real concurrent latency probe (TCP for TCP protocols, ICMP for UDP-based ones like Hysteria2).
        var tasks = Servers.Select(async server => server.Delay = await LatencyProbe.MeasureAsync(server, ct));

        await Task.WhenAll(tasks);
    }

}

public sealed class ModeRepository : IModeRepository
{
    public ModeRepository()
    {
        foreach (var m in SampleData.CreateModes())
            Modes.Add(m);
    }

    public ObservableCollection<Mode> Modes { get; } = new();

    public void Add(Mode mode) => Modes.Add(mode);

    public void Remove(Mode mode) => Modes.Remove(mode);
}

public sealed class SubscriptionRepository : ISubscriptionRepository
{
    private readonly HttpClient _http;
    private readonly IServerRepository _servers;

    public SubscriptionRepository(IServerRepository servers, HttpClient http)
    {
        _servers = servers;
        _http = http;

        foreach (var s in SampleData.CreateSubscriptions())
            Subscriptions.Add(s);
    }

    public ObservableCollection<Subscription> Subscriptions { get; } = new();

    public void Add(Subscription subscription) => Subscriptions.Add(subscription);

    public void Remove(Subscription subscription)
    {
        // Purge the servers this subscription imported (matched by the same group tag UpdateAsync used),
        // otherwise deleting a subscription leaves its nodes orphaned in the server list forever.
        var group = subscription.GroupKey;
        foreach (var stale in _servers.Servers.Where(s => s.Group == group).ToList())
            _servers.Remove(stale);

        Subscriptions.Remove(subscription);
    }

    // Many airport panels gate the raw (base64) node list behind a User-Agent allow-list, and 403
    // unknown clients. Try the widely-recognised ones and use the first response we can parse.
    private static readonly string[] UserAgents =
    {
        "v2rayN/6.45", "Shadowrocket/2.2.3", "NekoBox/1.3.8", "clash-verge/v1.6.0", "Netch/2.0"
    };

    /// <summary>
    ///     Downloads the subscription body, parses it into servers and replaces this subscription's
    ///     previously-imported servers (matched by group tag) in the shared server repository.
    /// </summary>
    public async Task UpdateAsync(Subscription subscription, CancellationToken ct = default)
    {
        var (parsed, lastError) = await FetchAndParseAsync(subscription.Link, ct);
        if (parsed.Count == 0 && lastError != null)
            throw lastError;

        var group = subscription.GroupKey;

        // Drop servers from a previous fetch of this subscription, then add the fresh set.
        foreach (var stale in _servers.Servers.Where(s => s.Group == group).ToList())
            _servers.Remove(stale);

        foreach (var server in parsed)
        {
            server.Group = group;
            _servers.Add(server);
        }

        subscription.ServerCount = parsed.Count;
        subscription.LastUpdated = DateTime.Now;
    }

    private async Task<(IReadOnlyList<Server> Parsed, Exception? LastError)> FetchAndParseAsync(string link, CancellationToken ct)
    {
        Exception? lastError = null;

        foreach (var ua in UserAgents)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, link);
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.ParseAdd(ua);
                // Browser-like Accept headers — Cloudflare (and similar WAFs) 403 requests without them.
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                var parsed = ShareLinkParser.ParseText(body);
                if (parsed.Count > 0)
                    return (parsed, null);

                // 200 but nothing we can parse (e.g. a Clash YAML variant) — try the next UA.
                lastError = new InvalidOperationException("Subscription returned no recognisable nodes.");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                lastError = e;
            }
        }

        return (Array.Empty<Server>(), lastError);
    }
}
