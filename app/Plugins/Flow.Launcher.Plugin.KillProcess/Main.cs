using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.KillProcess;

public class Main : IAsyncPlugin
{
    internal static PluginInitContext Context;

    /// <summary>
    /// 进程信息缓存（pid -> 名称/路径/中文描述）。
    /// 读 MainModule 和文件版本信息比较慢，缓存后每次查询只需比对字符串。
    /// </summary>
    private static readonly Dictionary<int, ProcInfo> _infoCache = new();
    private static readonly object _cacheLock = new();

    public Task InitAsync(PluginInitContext context)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        var backgroundOnly = string.Equals(query.ActionKeyword, "bkill", StringComparison.OrdinalIgnoreCase);
        var filter = (query.Search ?? string.Empty).Trim();

        var entries = new List<Entry>();
        var alive = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            if (token.IsCancellationRequested)
                break;

            try
            {
                var pid = process.Id;
                alive.Add(pid);

                var info = GetInfo(process);

                var matchScore = BestScore(filter, info);
                if (matchScore < 0)
                    continue;

                // MainWindowHandle 非 0 视为“有界面的前台程序”
                var hasWindow = process.MainWindowHandle != IntPtr.Zero;

                if (backgroundOnly && hasWindow)
                    continue;

                long memory = 0;
                try
                {
                    memory = process.WorkingSet64;
                }
                catch
                {
                    // 无权访问
                }

                entries.Add(new Entry
                {
                    HasWindow = hasWindow,
                    Memory = memory,
                    MatchScore = matchScore,
                    Result = new Result
                    {
                        Title = info.DisplayName,
                        SubTitle = $"PID {pid}  ·  {FormatMemory(memory)}" +
                                   (string.IsNullOrEmpty(info.Path) ? string.Empty : $"  ·  {info.Path}"),
                        IcoPath = info.Path,
                        Score = hasWindow ? 100 : 50,
                        Action = actionContext =>
                        {
                            // Shift+Enter：打开任务管理器
                            if (actionContext.SpecialKeyState.ShiftPressed)
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                                }
                                catch
                                {
                                    // ignored
                                }

                                return true;
                            }

                            return Kill(pid);
                        }
                    }
                });
            }
            catch
            {
                // 进程已退出或无权访问，跳过
            }
            finally
            {
                process.Dispose();
            }
        }

        PruneCache(alive);

        var results = entries
            .OrderByDescending(e => e.MatchScore)   // 匹配度高的排前面
            .ThenBy(e => e.HasWindow ? 0 : 1)       // 同分时有界面的应用优先
            .ThenByDescending(e => e.Memory)        // 再按内存占用从大到小
            .Select(e => e.Result)
            .ToList();

        return Task.FromResult(results);
    }

    /// <summary>
    /// 结束进程后保持窗口打开，方便连续清理多个进程
    /// </summary>
    private static bool Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill();
            process.WaitForExit(3000);
        }
        catch (Exception e)
        {
            Context.API.ShowMsgError("结束进程失败", e.Message);
            return false;
        }

        Context.API.ReQuery();
        return false;
    }

    private static ProcInfo GetInfo(Process process)
    {
        lock (_cacheLock)
        {
            if (_infoCache.TryGetValue(process.Id, out var cached))
                return cached;
        }

        var path = TryGetPath(process);

        var info = new ProcInfo
        {
            Name = process.ProcessName,
            Path = path,
            Description = TryGetDescription(path)
        };

        lock (_cacheLock)
        {
            _infoCache[process.Id] = info;
        }

        return info;
    }

    /// <summary>
    /// 匹配质量分，-1 表示不匹配。优先级：前缀 > 子串 > 子序列。
    /// 子序列用来覆盖首字母缩写，例如 "wx" -> "Weixin"（W...x... 按顺序出现即可）
    /// </summary>
    private static int MatchScore(string filter, string text)
    {
        if (string.IsNullOrEmpty(filter) || string.IsNullOrEmpty(text))
            return -1;

        if (text.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            return 100;

        if (text.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return 80;

        // 单字符查询不做子序列匹配，否则几乎会命中所有进程
        return filter.Length >= 2 && IsSubsequence(filter, text) ? 60 : -1;
    }

    private static bool IsSubsequence(string query, string target)
    {
        var index = 0;

        foreach (var c in target)
        {
            if (index < query.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(query[index]))
                index++;
        }

        return index == query.Length;
    }

    private static int BestScore(string filter, ProcInfo info)
    {
        if (string.IsNullOrEmpty(filter))
            return 0;

        var best = MatchScore(filter, info.Name);

        if (!string.IsNullOrEmpty(info.Description))
            best = Math.Max(best, MatchScore(filter, info.Description));

        // 兜底交给宿主：它会把中文转拼音再匹配（如 "wx" 命中中文描述"微信"）
        if (best < 0)
        {
            var hit = Context.API.FuzzySearch(filter, info.Name).Success ||
                      (!string.IsNullOrEmpty(info.Description) &&
                       Context.API.FuzzySearch(filter, info.Description).Success);

            if (hit)
                best = 50;
        }

        return best;
    }

    private static void PruneCache(HashSet<int> alive)
    {
        lock (_cacheLock)
        {
            foreach (var pid in _infoCache.Keys.Where(k => !alive.Contains(k)).ToList())
                _infoCache.Remove(pid);
        }
    }

    private static string TryGetDescription(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileDescription ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryGetPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatMemory(long bytes)
    {
        if (bytes <= 0)
            return "内存 N/A";

        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / 1024d / 1024 / 1024:F1} GB";

        return $"{bytes / 1024d / 1024:F0} MB";
    }

    private sealed class ProcInfo
    {
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// 优先显示中文描述，例如 "微信 (WeChat)"
        /// </summary>
        public string DisplayName =>
            string.IsNullOrEmpty(Description) ||
            Description.Equals(Name, StringComparison.OrdinalIgnoreCase)
                ? Name
                : $"{Description} ({Name})";
    }

    private sealed class Entry
    {
        public bool HasWindow { get; init; }
        public long Memory { get; init; }
        public int MatchScore { get; init; }
        public Result Result { get; init; } = null!;
    }
}
