using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Flow.Launcher.Plugin.Program.Views.Models;

namespace Flow.Launcher.Plugin.Program
{
    public class Settings
    {
        public DateTime LastIndexTime { get; set; }

        /// <summary>
        /// User-added program sources' directories
        /// </summary>
        // [闪启改造] 默认把桌面和常用绿色软件目录纳入扫描，否则 geek.exe 这类
        // 没写进开始菜单/注册表/PATH 的独立小程序永远搜不到
        public List<ProgramSource> ProgramSources { get; set; } = CreateDefaultSources();

        private static List<ProgramSource> CreateDefaultSources()
        {
            var sources = new List<ProgramSource>();

            void Add(string path, string name)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    return;

                if (sources.Any(s => string.Equals(s.Location, path, StringComparison.OrdinalIgnoreCase)))
                    return;

                sources.Add(new ProgramSource(path) { Name = name });
            }

            Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "桌面");
            Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "公共桌面");
            Add(@"D:\ZA", @"D:\ZA");

            return sources;
        }

        /// <summary>
        /// Disabled single programs, not including User-added directories
        /// </summary>
        public List<ProgramSource> DisabledProgramSources { get; set; } = new List<ProgramSource>();

        [Obsolete("Should use GetSuffixes() instead."), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[] ProgramSuffixes { get; set; } = null;
        public string[] CustomSuffixes { get; set; } = Array.Empty<string>();  // Custom suffixes only
        public string[] CustomProtocols { get; set; } = Array.Empty<string>();

        public Dictionary<string, bool> BuiltinSuffixesStatus { get; set; } = new Dictionary<string, bool>{
            { "exe", true }, { "appref-ms", true }, { "lnk", true }
        };

        public Dictionary<string, bool> BuiltinProtocolsStatus { get; set; } = new Dictionary<string, bool>{
            { "steam", true }, { "epic", true }, { "http", false }
        };

        [JsonIgnore]
        public Dictionary<string, string> BuiltinProtocols { get; set; } = new Dictionary<string, string>{
            { "steam", $"steam://run/{SuffixSeparator}steam://rungameid/" }, { "epic", "com.epicgames.launcher://apps/" }, { "http",  $"http://{SuffixSeparator}https://"}
        };

        public bool UseCustomSuffixes { get; set; } = false;
        public bool UseCustomProtocols { get; set; } = false;

        public string[] GetSuffixes()
        {
            RemoveRedundantSuffixes();
            List<string> extensions = new List<string>();
            foreach (var item in BuiltinSuffixesStatus)
            {
                if (item.Value)
                {
                    extensions.Add(item.Key);
                }
            }

            if (BuiltinProtocolsStatus.Values.Any(x => x == true) || UseCustomProtocols)
            {
                extensions.Add("url");
            }

            if (UseCustomSuffixes)
            {
                return extensions.Concat(CustomSuffixes).DistinctBy(x => x.ToLower()).ToArray();
            }
            else
            {
                return extensions.DistinctBy(x => x.ToLower()).ToArray();
            }
        }

        public string[] GetProtocols()
        {
            List<string> protocols = new List<string>();
            foreach (var item in BuiltinProtocolsStatus)
            {
                if (item.Value)
                {
                    if (BuiltinProtocols.TryGetValue(item.Key, out string ps))
                    {
                        var tmp = ps.Split(SuffixSeparator, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var protocol in tmp)
                        {
                            protocols.Add(protocol);
                        }
                    }
                }
            }

            if (UseCustomProtocols)
            {
                return protocols.Concat(CustomProtocols).DistinctBy(x => x.ToLower()).ToArray();
            }
            else
            {
                return protocols.DistinctBy(x => x.ToLower()).ToArray();
            }
        }

        private void RemoveRedundantSuffixes()
        {
            // Migrate to new settings
            // CustomSuffixes no longer contains custom suffixes
            // users has tweaked the settings
            // or this function has been executed once
            if (UseCustomSuffixes == true || ProgramSuffixes == null)
                return;
            var suffixes = ProgramSuffixes.ToList();
            foreach(var item in BuiltinSuffixesStatus)
            {
                suffixes.Remove(item.Key);
            }
            CustomSuffixes = suffixes.ToArray(); // Custom suffixes
            UseCustomSuffixes = CustomSuffixes.Length != 0; // Search custom suffixes or not
            ProgramSuffixes = null;
        }

        public bool EnableStartMenuSource { get; set; } = true;
        public bool EnableDescription { get; set; } = false;
        public bool HideAppsPath { get; set; } = true;
        public bool HideUninstallers { get; set; } = false;
        public bool EnableRegistrySource { get; set; } = true;
        public bool EnablePathSource { get; set; } = false;
        public bool EnableUWP { get; set; } = true;
        public bool HideDuplicatedWindowsApp { get; set; } = false;

        internal const char SuffixSeparator = ';';
    }
}
