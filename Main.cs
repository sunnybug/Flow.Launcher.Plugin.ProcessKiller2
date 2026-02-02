using System;
using System.Collections.Generic;
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

        /// <summary>处理用户查询并返回结果列表。</summary>
        public List<Result> Query(Query query)
        {
            return new List<Result>();
        }
    }
}
