using Netch.Core.Enums;
using Netch.Core.Utils;

namespace Netch.Host;

/// <summary>
///     Background subscription refresher. Checks 60 s after startup, then every 10 minutes, and refreshes
///     every subscription whose <c>LastUpdated</c> is null or older than the configured interval.
///     Never holds the state lock across network I/O (RefreshSubscriptionAsync does the fetch outside it)
///     and never logs subscription URLs — only the remark/id.
/// </summary>
internal sealed class SubscriptionScheduler
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    private readonly RpcRouter _router;
    private readonly Netch.Core.Abstractions.ILogService _log;

    public SubscriptionScheduler(RpcRouter router, Netch.Core.Abstractions.ILogService log)
    {
        _router = router;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                SafeLog(LogLevel.Warning, "订阅自动更新检查失败：" + e.Message);
            }

            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var hours = await _router.GetSubscriptionAutoUpdateHoursAsync(ct);
        if (hours <= 0)
            return; // 0 = disabled

        var interval = TimeSpan.FromHours(hours);
        var now = DateTime.Now;
        var snapshot = await _router.SnapshotSubscriptionsAsync(ct);

        foreach (var (id, lastUpdated) in snapshot)
        {
            if (ct.IsCancellationRequested)
                return;

            if (lastUpdated is { } last && now - last < interval)
                continue;

            try
            {
                await _router.RefreshSubscriptionAsync(id, ct);
                SafeLog(LogLevel.Info, $"订阅已自动更新：{id}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Never log the URL — only the id and the (already redacted) reason.
                SafeLog(LogLevel.Warning, $"订阅自动更新失败：{id}（{Redact(e.Message)}）");
            }
        }
    }

    /// <summary>Best-effort redaction of anything that looks like a URL in an error message.</summary>
    private static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
            return "";
        var index = message.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return message;
        var end = message.IndexOf(' ', index);
        var url = end < 0 ? message[index..] : message[index..end];
        return message.Replace(url, SecretRedactor.RedactUrl(url));
    }

    private void SafeLog(LogLevel level, string message)
    {
        try
        {
            _log.Log(level, message);
        }
        catch
        {
            // best effort
        }
    }
}
