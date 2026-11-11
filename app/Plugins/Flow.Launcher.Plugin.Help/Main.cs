using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.Help;

public class Main : IAsyncPlugin, IAsyncHomeQuery
{
    internal static PluginInitContext Context;

    public Task InitAsync(PluginInitContext context)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        return Task.FromResult(BuildHelp());
    }

    public Task<List<Result>> HomeQueryAsync(CancellationToken token)
    {
        return Task.FromResult(BuildHelp());
    }

    private static List<Result> BuildHelp()
    {
        return new List<Result>
        {
            Item("应用搜索",
                "直接输入程序名，例如 微信 / chrome / geek / Everything",
                "Images\\search.png",
                string.Empty),

            Item("文件导航",
                "f + 路径或关键字：f D:\\ZA 进目录，f DD 直接跳到 D:\\；Enter 逐级进入，Shift+Enter 打开资源管理器",
                "Images\\folder.png",
                "f "),

            Item("结束进程",
                "kill 列出进程（前台优先），bkill 只看后台；Enter 结束且窗口不关，Shift+Enter 打开任务管理器",
                "Images\\cmd.png",
                "kill "),

            Item("网页搜索",
                "g 谷歌 / bd 百度 / bing / github / wiki / translate / stackoverflow，例如 bing 今天天气",
                "Images\\Link.png",
                "bing "),

            Item("浏览器书签",
                "b + 关键词，例如 b 淘宝",
                "Images\\Browser.png",
                "b "),

            Item("常用按键",
                "Ctrl+I 打开设置；Ctrl+Enter 打开所在目录；Ctrl+Shift+Enter 管理员运行；PageUp/PageDown 翻页",
                "Images\\settings.png",
                string.Empty)
        };
    }

    private static Result Item(string title, string subtitle, string icon, string fillQuery)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = icon,
            Score = 100,
            Action = _ =>
            {
                // 点击后把关键字填进输入框，方便直接上手
                if (!string.IsNullOrEmpty(fillQuery))
                    Context.API.ChangeQuery(fillQuery, true);

                return false;
            }
        };
    }
}
