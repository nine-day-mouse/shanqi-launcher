using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.FileManager;

public class Main : IAsyncPlugin
{
    private const string Keyword = "f";

    /// <summary>
    /// 命中这些路径片段的目录视为噪音，不展示（否则 Everything 会返回一堆
    /// .git / node_modules / 回收站 之类的深层目录，把真正想要的短路径挤下去）
    /// </summary>
    private static readonly string[] NoiseSegments =
    {
        @"\node_modules\",
        @"\.git\",
        @"\.cache\",
        @"\$Recycle.Bin",
        @"\AppData\Local\Temp\",
        @"\.venv\",
        @"\.nuget\",
        @"\.codebuddy\",
        @"\.workbuddy\",
    };

    internal static PluginInitContext Context;

    private Settings _settings;
    private string _esExe;

    // 记录上一次盘符补全，避免用户手动删掉 ":\" 后又被立刻补回去
    private static string _lastDriveCompletion = string.Empty;
    private static DateTime _lastDriveCompletionTime = DateTime.MinValue;

    public Task InitAsync(PluginInitContext context)
    {
        Context = context;
        _settings = context.API.LoadSettingJsonStorage<Settings>();

        var bundled = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "tools", "es.exe");

        if (!string.IsNullOrWhiteSpace(_settings.EverythingExePath) && File.Exists(_settings.EverythingExePath))
            _esExe = _settings.EverythingExePath;
        else if (File.Exists(bundled))
            _esExe = bundled;

        return Task.CompletedTask;
    }

    public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        var search = (query.Search ?? string.Empty).Trim();

        // 防御：宿主正常情况下会把 "f " 关键字剥掉，万一没剥（或关键字被当作普通词），这里再兜一次
        if (search.StartsWith(Keyword + " ", StringComparison.OrdinalIgnoreCase))
            search = search[(Keyword.Length + 1)..].TrimStart();

        // 空白查询：只有显式用 f 关键字时才给盘符/常用目录
        if (string.IsNullOrEmpty(search))
            return IsFileKeyword(query) ? BuildDrives() : new List<Result>();

        // 输入看起来是路径：直接在启动器里列目录
        if (IsPathLike(search))
            return BuildDirectoryListing(search);

        // 输入两个相同字母（f DD / f CC）时补全成盘符根目录，
        // 单个字母不补全，因为 D 开头的文件夹名很常见。
        // 补全后 4 秒内不再对同一内容重复补全，否则用户删掉 ":\" 会被立刻补回去，
        // 导致永远输不进 DD 这类命名的文件夹。
        if (search.Length == 2 &&
            char.IsLetter(search[0]) &&
            char.ToUpperInvariant(search[0]) == char.ToUpperInvariant(search[1]))
        {
            var justCompleted = search.Equals(_lastDriveCompletion, StringComparison.OrdinalIgnoreCase) &&
                                (DateTime.Now - _lastDriveCompletionTime).TotalSeconds < 4;

            if (!justCompleted)
            {
                var drive = char.ToUpperInvariant(search[0]) + ":\\";
                if (Directory.Exists(drive))
                {
                    _lastDriveCompletion = search;
                    _lastDriveCompletionTime = DateTime.Now;
                    Context.API.ChangeQuery($"{Keyword} {drive}", true);
                    return new List<Result>();
                }
            }
        }

        // 至少两个字符才去查 Everything，避免每次敲键都起进程
        if (search.Length < 2)
            return new List<Result>();

        return await SearchFoldersAsync(search, token).ConfigureAwait(false);
    }

    private static bool IsFileKeyword(Query query) =>
        string.Equals(query.ActionKeyword, Keyword, StringComparison.OrdinalIgnoreCase);

    private static bool IsPathLike(string search)
    {
        if (search.Length >= 2 && search[1] == ':')
            return true;

        return search.StartsWith(@"\\", StringComparison.Ordinal);
    }

    #region 目录浏览

    private List<Result> BuildDirectoryListing(string input)
    {
        var normalized = input.Replace('/', '\\').TrimEnd('\\');

        // "D:" 单独出现时补回根斜杠，否则 .NET 会把它当成"当前工作目录"
        if (normalized.Length == 2 && normalized[1] == ':')
            normalized += "\\";

        if (!Directory.Exists(normalized))
            return SuggestSubDirectories(input);

        var results = new List<Result>();

        // 返回上级
        var parent = Path.GetDirectoryName(normalized);
        if (!string.IsNullOrEmpty(parent))
        {
            if (parent.Length == 2 && parent[1] == ':')
                parent += "\\";
            results.Add(FolderResult(parent, "..", "返回上级目录", 120));
        }

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(normalized)
                         .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                         .Take(300))
            {
                results.Add(FolderResult(dir, Path.GetFileName(dir), dir, 100));
            }

            if (_settings.ShowFiles)
            {
                foreach (var file in Directory.EnumerateFiles(normalized)
                             .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                             .Take(300))
                {
                    results.Add(FileResult(file));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return results;
    }

    /// <summary>
    /// 路径还不完整时（例如 D:\ZA\Obs 还没敲完），列出父目录下匹配的子目录，实现边输边补全
    /// </summary>
    private List<Result> SuggestSubDirectories(string input)
    {
        var normalized = input.Replace('/', '\\').TrimEnd('\\');
        var lastSep = normalized.LastIndexOf('\\');

        if (lastSep <= 0)
            return new List<Result>();

        var parentDir = normalized[..lastSep];
        var prefix = normalized[(lastSep + 1)..];

        if (parentDir.Length == 2 && parentDir[1] == ':')
            parentDir += "\\";

        if (!Directory.Exists(parentDir))
            return new List<Result>();

        var results = new List<Result>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parentDir)
                         .Where(d => Path.GetFileName(d).Contains(prefix, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(d => d.Length))
            {
                results.Add(FolderResult(dir, Path.GetFileName(dir), dir, 100));
                if (results.Count >= 20)
                    break;
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return results;
    }

    private static List<Result> BuildDrives()
    {
        var results = new List<Result>();

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            results.Add(FolderResult(drive.RootDirectory.FullName, drive.Name, drive.DriveType.ToString(), 110));
        }

        AddSpecialFolder(results, Environment.SpecialFolder.Desktop, "桌面");
        AddSpecialFolder(results, Environment.SpecialFolder.MyDocuments, "文档");
        AddSpecialFolder(results,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "下载");

        return results;
    }

    private static void AddSpecialFolder(List<Result> results, Environment.SpecialFolder folder, string title)
    {
        var path = Environment.GetFolderPath(folder);
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            results.Add(FolderResult(path, title, path, 100));
    }

    private static void AddSpecialFolder(List<Result> results, string path, string title)
    {
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            results.Add(FolderResult(path, title, path, 100));
    }

    #endregion

    #region Everything 搜索

    private async Task<List<Result>> SearchFoldersAsync(string search, CancellationToken token)
    {
        if (string.IsNullOrEmpty(_esExe))
            return new List<Result>();

        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var lines = await RunEsAsync(search, token).ConfigureAwait(false);

        // 多词 AND 搜不到时，退化成“按顺序出现的通配匹配”，例如 doc za -> doc*za
        if (lines.Count == 0 && terms.Length > 1)
            lines = await RunEsAsync(string.Join("*", terms), token).ConfigureAwait(false);

        if (lines.Count == 0)
            return new List<Result>();

        // path -> 名称是否命中搜索词
        var candidates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (IsNoise(line))
                continue;

            if (!candidates.ContainsKey(line))
                candidates[line] = NameMatchesTerms(line, terms);

            // Everything 有时索引不到某些上层目录（比如 D:\ZA\Github_program 本身搜不到，
            // 只能搜到它的子目录），这里把名称命中的祖先目录补回候选集
            AddMatchingParents(candidates, line, terms);
        }

        var max = _settings.MaxResults > 0 ? _settings.MaxResults : 8;

        var picked = candidates
            .OrderByDescending(kv => kv.Value)            // 目录名本身命中搜索词的优先
            .ThenBy(kv => kv.Key.Length)                  // 其次路径越短层级越浅
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();

        var results = new List<Result>();
        var score = 100;

        foreach (var pair in picked)
        {
            var path = pair.Key;
            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name))
                name = path;

            results.Add(FolderResult(path, name, path, score));
            score -= 4;
        }

        return results;
    }

    private async Task<List<string>> RunEsAsync(string query, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _esExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add("1000");
        psi.ArgumentList.Add("-ad");                 // 只返回文件夹

        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            psi.ArgumentList.Add(term);

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
            {
                TryKill(process);
                token.ThrowIfCancellationRequested();
            }

            await process.WaitForExitAsync(token).ConfigureAwait(false);

            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().TrimEnd('\r'))
                .Where(line => line.Length > 0 && Directory.Exists(line))
                .ToList();
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception)
        {
            TryKill(process);
            return new List<string>();
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// 目录名是否包含全部搜索词（大小写不敏感）
    /// </summary>
    private static bool NameMatchesTerms(string path, string[] terms)
    {
        if (terms.Length == 0)
            return false;

        var name = Path.GetFileName(path.TrimEnd('\\'));
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var term in terms)
        {
            if (!name.Contains(term, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 把名称命中的祖先目录补进候选集（最多向上 8 层），
    /// 解决 Everything 索引缺失上层目录导致只能看到子目录的问题
    /// </summary>
    private static void AddMatchingParents(Dictionary<string, bool> candidates, string path, string[] terms)
    {
        var dir = Path.GetDirectoryName(path.TrimEnd('\\'));

        for (var guard = 0; guard < 8 && !string.IsNullOrEmpty(dir); guard++)
        {
            if (NameMatchesTerms(dir, terms) && Directory.Exists(dir) && !candidates.ContainsKey(dir))
                candidates[dir] = true;

            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;

            dir = parent;
        }
    }

    private static bool IsNoise(string path)
    {
        foreach (var segment in NoiseSegments)
        {
            if (path.Contains(segment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var name = Path.GetFileName(path.TrimEnd('\\'));
        return !string.IsNullOrEmpty(name) && name.StartsWith('.');
    }

    #endregion

    #region 结果构造

    private static Result FolderResult(string fullPath, string title, string subtitle, int score)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = fullPath,
            Score = score,
            Action = context =>
            {
                // Shift+Enter：在资源管理器里打开
                if (context.SpecialKeyState.ShiftPressed)
                {
                    Context.API.OpenDirectory(fullPath);
                    return true;
                }

                // Enter：进入该目录，继续在启动器里浏览
                Context.API.ChangeQuery($"{Keyword} {fullPath.TrimEnd('\\')}\\", true);
                return false;
            }
        };
    }

    private static Result FileResult(string fullPath)
    {
        return new Result
        {
            Title = Path.GetFileName(fullPath),
            SubTitle = fullPath,
            IcoPath = fullPath,
            Score = 90,
            Action = context =>
            {
                if (context.SpecialKeyState.ShiftPressed)
                {
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir))
                        Context.API.OpenDirectory(dir);
                    return true;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                }
                catch
                {
                    // ignored
                }

                return true;
            }
        };
    }

    #endregion
}
