namespace Netch.Core.Services.Diagnostics;

/// <summary>
///     Maps a raw backend error string to a plain-Chinese explanation. Best-effort substring matching:
///     unknown errors fall back to a generic message pointing at the check results.
/// </summary>
public static class ErrorExplainer
{
    public static string Explain(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "未能识别的错误，请参考下方检测结果";

        var e = error;

        if (Contains(e, "netfilter") || Contains(e, "驱动") || Contains(e, "Redirector"))
            return "进程模式驱动未能加载，可能被安全软件拦截或缺少管理员权限。请以管理员身份运行，或将 Game+ 加入安全软件白名单。";

        if (Contains(e, "xray did not start") || Contains(e, "sing-box"))
            return "加速核心启动失败，通常是被杀毒软件拦截或本地端口被占用。请检查杀毒软件隔离区，或重启后重试。";

        if (Contains(e, "没有可用线路"))
            return "还没有可用的线路。请前往“订阅”页导入订阅，或在“线路”页添加分享链接。";

        if (Contains(e, "线路不存在"))
            return "之前选择的线路已被删除。请重新选择一条线路，或开启智能选路。";

        if (Contains(e, "加速规则"))
            return "该游戏没有配置加速规则，无法确定要加速的域名或进程。请在游戏库中编辑该游戏并补充规则。";

        if (Contains(e, "timeout") || Contains(e, "超时"))
            return "连接超时，可能是本机网络不稳定或线路不可用。请检查网络连接，或更换线路后重试。";

        if (Contains(e, "拒绝访问") || Contains(e, "Access is denied") || Contains(e, "UnauthorizedAccess"))
            return "操作被系统拒绝，通常是因为缺少管理员权限。请退出 Game+ 后以管理员身份重新打开。";

        return "未能识别的错误，请参考下方检测结果";
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
