---
name: plugin-debug-check-logs
description: Agent 完成后运行 debug.ps1、修复出现的问题；若运行成功则等待 10 秒后检查 Flow Launcher Logs 下最新日志中本插件（ProcessKiller2）的错误。用于 Flow Launcher 插件开发时的调试与日志检查。
---

# 插件调试与日志检查

## 触发场景

- 用户说「agent 完成后运行 debug 并检查日志」
- 用户执行 `/create-skill` 并指定「运行 debug.ps1，解决出现的问题，检查 Logs」
- Flow Launcher 插件开发完成后需要验证安装与运行

## 工作流程

### 1. 运行 debug.ps1

在项目根目录执行：

```powershell
.\debug.ps1
```

可选指定配置：`.\debug.ps1 -Configuration Release`

### 2. 解决出现的问题

- 若 `dotnet restore` / `dotnet build` 失败：根据报错修代码或依赖，直至编译通过。
- 若 Flow Launcher 未完全退出导致文件占用：脚本会尝试关闭进程；若仍失败，提示用户手动关闭 Flow Launcher 后重试。
- 若复制或目录操作失败：检查 `$env:APPDATA\FlowLauncher\Plugins` 权限与路径。

### 3. 运行成功后的日志检查

仅当 debug.ps1 **退出码为 0** 时执行：

1. **等待 10 秒**：`Start-Sleep -Seconds 10`（便于 Flow Launcher 启动并产生新日志）
2. **定位最新日志**：
   - 日志根目录：`$env:APPDATA\FlowLauncher\Logs`
   - 其下可能有按 **Flow Launcher 版本号** 命名的子目录（如 `1.0.0`），也可能直接在根目录。
   - 取该目录（及其子目录）下**最新修改时间**的 `.log` 或 `.txt` 文件作为「最新 log 文件」。
3. **筛选本插件相关错误**：
   - 在最新 log 文件中查找与 **ProcessKiller2** 或插件 ID **88db58ee360d9d08a824a8e75ba2e63c** 相关的行。
   - 重点查找：Exception、Error、Failed、插件加载失败、插件执行异常等关键字。

### 4. 输出与后续

- 汇总 debug.ps1 运行结果（成功/失败及原因）。
- 若执行了日志检查：列出最新 log 文件路径，并摘录/总结与本插件相关的错误或异常；若无相关错误则说明「未发现本插件错误」。

## 日志路径说明

| 项     | 值 |
|--------|-----|
| 日志根目录 | `$env:APPDATA\FlowLauncher\Logs` |
| 版本子目录 | 可能存在 `Logs\版本号\`（版本号为 Flow Launcher 应用版本） |
| 最新文件   | 在上述范围内按 LastWriteTime 取最新的 `.log`/`.txt` |

## 本插件标识

- 插件名：**ProcessKiller2**
- 插件 ID：**88db58ee360d9d08a824a8e75ba2e63c**
- 日志中可能出现的名称：ProcessKiller2、Flow.Launcher.Plugin.ProcessKiller2、上述 ID
