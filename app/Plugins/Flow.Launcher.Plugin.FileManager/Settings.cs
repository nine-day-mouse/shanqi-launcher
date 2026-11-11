namespace Flow.Launcher.Plugin.FileManager;

public class Settings
{
    /// <summary>
    /// 自定义 es.exe 路径。留空则使用插件目录下的 tools\es.exe
    /// </summary>
    public string EverythingExePath { get; set; } = string.Empty;

    /// <summary>
    /// 搜索结果最多显示几条
    /// </summary>
    public int MaxResults { get; set; } = 8;

    /// <summary>
    /// 目录浏览时是否同时列出文件
    /// </summary>
    public bool ShowFiles { get; set; } = false;
}
