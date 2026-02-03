using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.ProcessKiller2.Properties;

namespace Flow.Launcher.Plugin.ProcessKiller2
{
    /// <summary>Win32 枚举窗口，按进程收集所有可见窗口标题。</summary>
    internal static class WindowEnumHelper
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>枚举所有顶层可见窗口，按进程 ID 收集非空标题。返回 pid -> 该进程下所有窗口标题列表。</summary>
        public static Dictionary<int, List<string>> GetAllWindowTitlesByProcess()
        {
            var byPid = new Dictionary<int, List<string>>();
            var sb = new StringBuilder(512);

            bool Callback(IntPtr hWnd, IntPtr _)
            {
                if (!IsWindowVisible(hWnd))
                    return true;
                if (GetWindowThreadProcessId(hWnd, out uint pid) == 0)
                    return true;
                sb.Clear();
                if (GetWindowText(hWnd, sb, sb.Capacity) <= 0)
                    return true;
                var title = sb.ToString().Trim();
                if (string.IsNullOrEmpty(title))
                    return true;
                int pidInt = (int)pid;
                if (!byPid.TryGetValue(pidInt, out var list))
                {
                    list = new List<string>();
                    byPid[pidInt] = list;
                }
                list.Add(title);
                return true;
            }

            EnumWindows(Callback, IntPtr.Zero);
            return byPid;
        }
    }
    /// <summary>Flow Launcher 插件：进程结束（ProcessKiller2）。</summary>
    public class ProcessKiller2 : IPlugin, IPluginI18n
    {
        private PluginInitContext _context;

        /// <summary>由 Flow Launcher 调用，初始化插件上下文。</summary>
        public void Init(PluginInitContext context)
        {
            _context = context;
        }

        /// <summary>插件显示名称（随语言切换）。</summary>
        public string Name => GetTranslatedPluginTitle();

        /// <summary>插件描述（随语言切换）。</summary>
        public string Description => GetTranslatedPluginDescription();

        /// <inheritdoc />
        public void OnCultureInfoChanged(CultureInfo newCulture)
        {
            Resources.Culture = newCulture;
        }

        /// <inheritdoc />
        public string GetTranslatedPluginTitle() => Resources.PluginTitle;

        /// <inheritdoc />
        public string GetTranslatedPluginDescription() => Resources.PluginDescription;

        /// <summary>处理用户查询并返回结果列表。若输入为数字则视为端口，只列出监听该端口的进程；否则按进程名匹配。</summary>
        public List<Result> Query(Query query)
        {
            var search = (query?.Search ?? "").Trim();
            // 空查询不显示列表，仅在有输入时再触发
            if (string.IsNullOrEmpty(search))
                return new List<Result>();

            List<(int Pid, string Name, string SubTitleOrPath, string WindowTitle, string FileKeyInfo)> items;
            var isPortQuery = int.TryParse(search, out int port) && port >= 1 && port <= 65535;
            if (isPortQuery)
            {
                var pidsOnPort = GetPidsListeningOnPort(port);
                if (pidsOnPort.Count == 0)
                    return new List<Result> { new Result { Title = string.Format(CultureInfo.CurrentUICulture, Resources.PortNoProcess, port), SubTitle = Resources.PortCheckOrSearchByName, IcoPath = "icon.png" } };
                items = LoadProcessInfosFilteredByPids(pidsOnPort);
            }
            else
            {
                items = LoadProcessInfos(search);
            }

            var results = new List<Result>();

            foreach (var (pid, name, subTitleOrPath, windowTitle, fileKeyInfo) in items)
            {
                int score = isPortQuery ? 100 : ComputeScore(name, search);
                if (score < 0)
                    continue;

                // 用局部变量保存 pid，确保回车时 Action 杀死的是当前选中项对应的进程
                int pidToKill = pid;
                var title = !string.IsNullOrEmpty(windowTitle)
                    ? windowTitle
                    : $"{name} (PID: {pid})";
                if (isPortQuery)
                    title = title + string.Format(CultureInfo.CurrentUICulture, Resources.PortTitleSuffix, port);
                var isCritical = CriticalProcessNames.Contains(name);
                var pathOrAction = subTitleOrPath ?? Resources.KillProcess;
                var keyInfoSuffix = string.IsNullOrEmpty(fileKeyInfo) ? "" : " · " + fileKeyInfo;
                var subTitle = isPortQuery
                    ? $"{name} (PID: {pid}) · " + string.Format(CultureInfo.CurrentUICulture, Resources.ListeningPortFormat, port) + " · " + (isCritical ? Resources.CriticalNoKill : pathOrAction + keyInfoSuffix)
                    : $"{name} (PID: {pid}) · " + pathOrAction + keyInfoSuffix;

                var result = new Result
                {
                    Title = title,
                    SubTitle = subTitle,
                    IcoPath = string.IsNullOrEmpty(subTitleOrPath) || subTitleOrPath == Resources.KillProcess ? "icon.png" : subTitleOrPath,
                    Score = score,
                    Action = _ =>
                    {
                        if (isCritical)
                        {
                            _context.API.ShowMsg(Resources.CriticalProcess, Resources.CriticalNoKillMessage, "icon.png", false);
                            return false;
                        }
                        try
                        {
                            using var p = Process.GetProcessById(pidToKill);
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

            // 若查询与某进程名完全匹配（且非端口查询），增加“杀死 xxx 的所有实例（个数）”聚合结果
            var exactMatches = isPortQuery ? new List<(int Pid, string Name, string SubTitleOrPath, string WindowTitle, string FileKeyInfo)>() : items.Where(x => string.Equals(x.Name, search, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exactMatches.Count > 0)
            {
                var first = exactMatches[0];
                var name = first.Name;
                var count = exactMatches.Count;
                var pidsToKill = exactMatches.Select(x => x.Pid).Distinct().ToList();
                var icoPath = string.IsNullOrEmpty(first.SubTitleOrPath) || first.SubTitleOrPath == Resources.KillProcess ? "icon.png" : first.SubTitleOrPath;
                results.Add(new Result
                {
                    Title = string.Format(CultureInfo.CurrentUICulture, Resources.KillAllInstances, name, count),
                    SubTitle = Resources.KillAllInstancesSubtitle,
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
            "winlogon", "fontdrvhost", "dwm", "Registry", "Memory Compression","svchost"
        };

        /// <summary>加载当前可访问且与 search 匹配的进程信息。先按名称过滤再取路径/图标/版本信息，减少无效进程的昂贵操作。</summary>
        private static List<(int Pid, string Name, string SubTitleOrPath, string WindowTitle, string FileKeyInfo)> LoadProcessInfos(string search)
        {
            var list = new List<(int, string, string, string, string)>();
            var titlesByPid = WindowEnumHelper.GetAllWindowTitlesByProcess();

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
                    if (ComputeScore(name, search) < 0)
                        continue;

                    var pid = p.Id;
                    var path = GetProcessPath(p);
                    var subTitleOrPath = !string.IsNullOrEmpty(path) ? path : Resources.KillProcess;
                    var fileKeyInfo = GetFileKeyInfoFromPath(path);
                    if (titlesByPid.TryGetValue(pid, out var titles) && titles.Count > 0)
                    {
                        foreach (var title in titles)
                            list.Add((pid, name, subTitleOrPath, title, fileKeyInfo));
                    }
                    else
                    {
                        var windowTitle = GetProcessWindowTitle(p);
                        list.Add((pid, name, subTitleOrPath, windowTitle, fileKeyInfo));
                    }
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

        /// <summary>获取进程主模块路径；无权限时返回 null。</summary>
        private static string GetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>从可执行文件路径读取版本资源，返回产品名/描述/公司等关键信息。仅在有路径且文件存在时访问磁盘。</summary>
        private static string GetFileKeyInfoFromPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return "";
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                var product = vi.ProductName?.Trim();
                var description = vi.FileDescription?.Trim();
                var company = vi.CompanyName?.Trim();
                if (!string.IsNullOrEmpty(product))
                    return product;
                if (!string.IsNullOrEmpty(description))
                    return description;
                if (!string.IsNullOrEmpty(company))
                    return company;
            }
            catch
            {
                // 无权限或非 PE 文件
            }
            return "";
        }

        /// <summary>获取正在监听指定端口的进程 PID 列表。通过 netstat -ano 解析，包含 TCP IPv4 与 TCPv6。</summary>
        private static List<int> GetPidsListeningOnPort(int port)
        {
            var pids = new HashSet<int>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netstat.exe",
                    Arguments = "-ano",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return pids.ToList();
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                var portStr = port.ToString();
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!line.Contains("LISTENING", StringComparison.Ordinal)) continue;
                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5) continue;
                    // 格式: Proto LocalAddress ForeignAddress State PID（TCP/TCPv6 均为此格式）
                    if (!string.Equals(parts[0], "TCP", StringComparison.OrdinalIgnoreCase)) continue;
                    var localAddr = parts[1];
                    var state = parts[3];
                    if (state != "LISTENING" || !localAddr.EndsWith(":" + portStr, StringComparison.Ordinal))
                        continue;
                    if (int.TryParse(parts[4], out int pid) && pid > 0)
                        pids.Add(pid);
                }
            }
            catch
            {
                // 忽略 netstat 执行或解析异常
            }
            return pids.ToList();
        }

        /// <summary>仅加载指定 PID 列表的进程信息，用于端口查询场景。主模块路径只取一次。</summary>
        private static List<(int Pid, string Name, string SubTitleOrPath, string WindowTitle, string FileKeyInfo)> LoadProcessInfosFilteredByPids(List<int> pids)
        {
            var list = new List<(int, string, string, string, string)>();
            var titlesByPid = WindowEnumHelper.GetAllWindowTitlesByProcess();
            foreach (int pid in pids)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    var name = GetProcessDisplayName(p);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var path = GetProcessPath(p);
                    var subTitleOrPath = !string.IsNullOrEmpty(path) ? path : Resources.KillProcess;
                    var fileKeyInfo = GetFileKeyInfoFromPath(path);
                    if (titlesByPid.TryGetValue(pid, out var titles) && titles.Count > 0)
                    {
                        foreach (var title in titles)
                            list.Add((pid, name, subTitleOrPath, title, fileKeyInfo));
                    }
                    else
                    {
                        var windowTitle = GetProcessWindowTitle(p);
                        list.Add((pid, name, subTitleOrPath, windowTitle, fileKeyInfo));
                    }
                }
                catch
                {
                    // 进程可能已退出或无权限
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
