using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.ProcessKiller2
{
    /// <summary>Flow Launcher 插件：进程结束（ProcessKiller2）。</summary>
    public class ProcessKiller2 : IPlugin
    {
        private PluginInitContext _context;

        /// <summary>由 Flow Launcher 调用，初始化插件上下文。</summary>
        public void Init(PluginInitContext context)
        {
            _context = context;
        }

        /// <summary>处理用户查询并返回结果列表。触发关键字后加载所有进程，按可执行文件名（不含后缀）完全匹配、部分匹配计算权重。</summary>
        public List<Result> Query(Query query)
        {
            var search = (query?.Search ?? "").Trim();
            // 空查询不显示列表，仅在有输入时再触发
            if (string.IsNullOrEmpty(search))
                return new List<Result>();

            var items = LoadProcessInfos();
            var results = new List<Result>();

            foreach (var (pid, name, subTitleOrPath, windowTitle) in items)
            {
                int score = ComputeScore(name, search);
                if (score < 0)
                    continue;

                var title = !string.IsNullOrEmpty(windowTitle)
                    ? windowTitle
                    : $"{name} (PID: {pid})";
                var subTitle = $"{name} (PID: {pid}) · " + (subTitleOrPath ?? "结束进程");

                var result = new Result
                {
                    Title = title,
                    SubTitle = subTitle,
                    IcoPath = string.IsNullOrEmpty(subTitleOrPath) || subTitleOrPath == "结束进程" ? "icon.png" : subTitleOrPath,
                    Score = score,
                    Action = _ =>
                    {
                        try
                        {
                            using var p = Process.GetProcessById(pid);
                            p.Kill();
                            return true;
                        }
                        catch (Exception)
                        {
                            return false;
                        }
                    }
                };
                results.Add(result);
            }

            // 若查询与某进程名完全匹配，增加“杀死 xxx 的所有实例（个数）”聚合结果
            var exactMatches = items.Where(x => string.Equals(x.Name, search, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exactMatches.Count > 0)
            {
                var first = exactMatches[0];
                var name = first.Name;
                var count = exactMatches.Count;
                var pidsToKill = exactMatches.Select(x => x.Pid).ToList();
                var icoPath = string.IsNullOrEmpty(first.SubTitleOrPath) || first.SubTitleOrPath == "结束进程" ? "icon.png" : first.SubTitleOrPath;
                results.Add(new Result
                {
                    Title = $"杀死 {name} 的所有实例（{count}）",
                    SubTitle = "一次性结束以上所有进程",
                    IcoPath = icoPath,
                    Score = 101, // 高于单条完全匹配 100，排在首位
                    Action = _ =>
                    {
                        foreach (var pid in pidsToKill)
                        {
                            try
                            {
                                using var p = Process.GetProcessById(pid);
                                p.Kill();
                            }
                            catch
                            {
                                // 进程可能已退出或无权限
                            }
                        }
                        return true;
                    }
                });
            }

            return results.OrderByDescending(r => r.Score).ToList();
        }

        /// <summary>Windows 关键系统进程名（不含后缀），列表中不显示。</summary>
        private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "smss", "csrss", "wininit", "services", "lsass",
            "winlogon", "fontdrvhost", "dwm", "Registry", "Memory Compression"
        };

        /// <summary>加载当前所有可访问的进程信息，返回 (PID, 可执行文件名不含后缀, 副标题/路径, 主窗口标题)。不持有 Process 句柄。</summary>
        private static List<(int Pid, string Name, string SubTitleOrPath, string WindowTitle)> LoadProcessInfos()
        {
            var list = new List<(int, string, string, string)>();
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return list;
            }

            foreach (var p in processes)
            {
                try
                {
                    var name = GetProcessDisplayName(p);
                    if (string.IsNullOrEmpty(name) || CriticalProcessNames.Contains(name))
                        continue;
                    var pid = p.Id;
                    var subTitleOrPath = GetProcessSubTitle(p);
                    var windowTitle = GetProcessWindowTitle(p);
                    list.Add((pid, name, subTitleOrPath, windowTitle));
                }
                catch
                {
                    // 忽略无权限或已退出的进程
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }

            return list;
        }

        /// <summary>按可执行文件名（不含后缀）计算权重：完全匹配 &gt; 部分匹配；不匹配返回 -1。空搜索时返回正分以便 Flow 显示结果。</summary>
        private static int ComputeScore(string processName, string search)
        {
            if (string.IsNullOrEmpty(processName))
                return -1;

            var name = processName.AsSpan();
            var term = search.AsSpan();
            var nameLower = processName.ToLowerInvariant();
            var termLower = search.ToLowerInvariant();

            if (string.Equals(nameLower, termLower, StringComparison.Ordinal))
                return 100;

            var idx = nameLower.IndexOf(termLower, StringComparison.Ordinal);
            if (idx >= 0)
                return 50 - idx;

            return -1;
        }

        /// <summary>获取进程显示名。Windows 上 ProcessName 可能为空，则从主模块路径解析。</summary>
        private static string GetProcessDisplayName(Process process)
        {
            var name = process.ProcessName?.Trim();
            if (!string.IsNullOrEmpty(name))
                return name;
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                    return Path.GetFileNameWithoutExtension(path);
            }
            catch
            {
                // 无权限或 32/64 位差异时 MainModule 会抛
            }
            return "";
        }

        private static string GetProcessSubTitle(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? "结束进程";
            }
            catch
            {
                return "结束进程";
            }
        }

        /// <summary>获取进程主窗口标题；无窗口或无权限时返回空字符串。</summary>
        private static string GetProcessWindowTitle(Process process)
        {
            try
            {
                var title = process.MainWindowTitle?.Trim();
                return title ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
