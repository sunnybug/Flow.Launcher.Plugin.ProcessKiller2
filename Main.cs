using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var items = LoadProcessInfos();
            var results = new List<Result>();

            foreach (var (pid, name, subTitle) in items)
            {
                int score = ComputeScore(name, search);
                if (score < 0)
                    continue;

                var result = new Result
                {
                    Title = $"{name} (PID: {pid})",
                    SubTitle = subTitle,
                    IcoPath = "icon.png",
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

            return results.OrderByDescending(r => r.Score).ToList();
        }

        /// <summary>加载当前所有可访问的进程信息，返回 (PID, 可执行文件名不含后缀, 副标题)。不持有 Process 句柄。</summary>
        private static List<(int Pid, string Name, string SubTitle)> LoadProcessInfos()
        {
            var list = new List<(int, string, string)>();
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
                    var name = p.ProcessName ?? "";
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var pid = p.Id;
                    var subTitle = GetProcessSubTitle(p);
                    list.Add((pid, name, subTitle));
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

            // 空搜索：返回正分，避免 Flow Launcher 过滤掉 Score<=0 的结果导致不显示
            if (string.IsNullOrEmpty(search))
                return 100;

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
    }
}
