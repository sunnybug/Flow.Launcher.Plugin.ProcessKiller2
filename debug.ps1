# 编译并安装 Flow Launcher 插件脚本

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

# 从 plugin.json 读取插件信息
$PluginJsonPath = "plugin.json"
$PluginConfig = Get-Content $PluginJsonPath -Raw | ConvertFrom-Json
$PluginID = $PluginConfig.ID
$PluginName = $PluginConfig.Name
$PluginVersion = $PluginConfig.Version

# 编译输出路径（dotnet build 默认输出到 bin\Configuration）
$OutputPath = "bin\$Configuration"

# Flow Launcher 插件目录
$FlowLauncherPluginsPath = Join-Path $env:APPDATA "FlowLauncher\Plugins"

# 使用带版本号的插件目录名（与其他插件保持一致）
$PluginFolderName = "$PluginName-$PluginVersion"
$TargetPath = Join-Path $FlowLauncherPluginsPath $PluginFolderName

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Flow Launcher 插件编译安装脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "插件名称: $PluginName" -ForegroundColor White
Write-Host "插件版本: $PluginVersion" -ForegroundColor White
Write-Host "插件 ID: $PluginID" -ForegroundColor White
Write-Host "编译配置: $Configuration" -ForegroundColor White
Write-Host ""

# 1. 还原依赖
Write-Host "[1/8] 正在还原 NuGet 包..." -ForegroundColor Yellow
dotnet restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "错误: dotnet restore 失败" -ForegroundColor Red
    exit 1
}
Write-Host "完成" -ForegroundColor Green
Write-Host ""

# 2. 编译项目
Write-Host "[2/8] 正在编译项目 ($Configuration)..." -ForegroundColor Yellow
dotnet build -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "错误: dotnet build 失败" -ForegroundColor Red
    exit 1
}
Write-Host "完成" -ForegroundColor Green
Write-Host ""

# 3. 检查编译输出
Write-Host "[3/8] 检查编译输出..." -ForegroundColor Yellow
if (-not (Test-Path $OutputPath)) {
    Write-Host "错误: 输出目录不存在: $OutputPath" -ForegroundColor Red
    exit 1
}
Write-Host "输出目录: $OutputPath" -ForegroundColor Gray
Write-Host "完成" -ForegroundColor Green
Write-Host ""

# 4. 关闭 Flow Launcher
Write-Host "[4/8] 关闭 Flow Launcher..." -ForegroundColor Yellow

# 检查并终止所有可能的 Flow Launcher 相关进程
$FlowLauncherProcesses = Get-Process -Name "Flow.Launcher" -ErrorAction SilentlyContinue
if ($FlowLauncherProcesses) {
    Write-Host "发现 $($FlowLauncherProcesses.Count) 个 Flow Launcher 进程" -ForegroundColor Gray
    foreach ($Process in $FlowLauncherProcesses) {
        try {
            $Process.CloseMainWindow() | Out-Null
            Start-Sleep -Milliseconds 500
            if (-not $Process.HasExited) {
                Write-Host "强制终止进程 PID: $($Process.Id)" -ForegroundColor Gray
                $Process.Kill()
            }
            $Process.WaitForExit(5000)
        } catch {
            Write-Host "终止进程失败: $_" -ForegroundColor Red
        }
    }
    Write-Host "已关闭所有 Flow Launcher 进程" -ForegroundColor Green
} else {
    Write-Host "Flow Launcher 未运行" -ForegroundColor Gray
}

# 等待 Flow Launcher 完全退出并释放文件句柄
Write-Host "等待进程完全退出..." -ForegroundColor Gray
$MaxWaitTime = 10  # 最多等待10秒
$Waited = 0
while ($Waited -lt $MaxWaitTime) {
    $StillRunning = Get-Process -Name "Flow.Launcher" -ErrorAction SilentlyContinue
    if (-not $StillRunning) {
        break
    }
    Write-Host "  进程仍在运行，等待中... ($Waited/$MaxWaitTime)" -ForegroundColor Gray
    Start-Sleep -Seconds 1
    $Waited++
}

# 额外等待3秒确保文件句柄释放
Start-Sleep -Seconds 3

# 再次检查
$FinalCheck = Get-Process -Name "Flow.Launcher" -ErrorAction SilentlyContinue
if ($FinalCheck) {
    Write-Host "警告: Flow Launcher 进程仍在运行！" -ForegroundColor Red
    foreach ($proc in $FinalCheck) {
        Write-Host "  PID: $($proc.Id), Path: $($proc.Path)" -ForegroundColor Red
    }
} else {
    Write-Host "进程已完全退出" -ForegroundColor Green
}
Write-Host ""

# 5. 清理日志文件
Write-Host "[5/8] 清理 Flow Launcher 日志..." -ForegroundColor Yellow
$FlowLauncherLogsPath = Join-Path $env:APPDATA "FlowLauncher\Logs"
$LogFiles = @()
if (Test-Path $FlowLauncherLogsPath) {
    $LogFiles = Get-ChildItem -Path $FlowLauncherLogsPath -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Extension -match '\.(txt|log)$' }
}
if ($LogFiles.Count -eq 0) {
    Write-Host "没有找到日志文件或目录不存在" -ForegroundColor Gray
} else {
    Write-Host "找到 $($LogFiles.Count) 个日志文件" -ForegroundColor Gray
    $DeletedCount = 0
    $FailedCount = 0
    foreach ($LogFile in $LogFiles) {
        $RelativePath = $LogFile.FullName.Replace($env:APPDATA, '%APPDATA%')
        try {
            Remove-Item -Path $LogFile.FullName -Force -ErrorAction Stop
            Write-Host "  OK 删除: $RelativePath" -ForegroundColor Gray
            $DeletedCount++
        } catch {
            $FailedCount++
            Write-Host "  删除失败: $RelativePath" -ForegroundColor Red
        }
    }
    if ($DeletedCount -gt 0) { Write-Host "成功删除 $DeletedCount 个日志文件" -ForegroundColor Green }
    if ($FailedCount -gt 0) { Write-Host "删除失败 $FailedCount 个日志文件" -ForegroundColor Red }
}
Write-Host ""

# 6. 清理旧的插件目录（避免重复插件问题）
Write-Host "[6/8] 清理旧的插件目录..." -ForegroundColor Yellow

# 可能的旧插件目录名称（不包括当前版本）
$OldPluginPaths = @(
    (Join-Path $FlowLauncherPluginsPath $PluginID),  # ID 目录
    (Join-Path $FlowLauncherPluginsPath "ProcessKiller2")   # 旧的固定名称目录（无版本号）
)

foreach ($OldPath in $OldPluginPaths) {
    if (Test-Path $OldPath) {
        Write-Host "删除旧目录: $OldPath" -ForegroundColor Gray
        Remove-Item -Path $OldPath -Recurse -Force
    }
}

# 删除其他版本的 ProcessKiller2-* 目录（仅保留当前版本目录）
Get-ChildItem -Path $FlowLauncherPluginsPath -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "ProcessKiller2-*" -and $_.Name -ne $PluginFolderName } | ForEach-Object {
    Write-Host "删除旧版本目录: $($_.FullName)" -ForegroundColor Gray
    Remove-Item -Path $_.FullName -Recurse -Force
}

# 创建或清空目标目录
if (-not (Test-Path $TargetPath)) {
    New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null
    Write-Host "创建插件目录: $TargetPath" -ForegroundColor Gray
} else {
    # 清空目标目录中的文件和子目录
    Get-ChildItem -Path $TargetPath -File | Remove-Item -Force
    Get-ChildItem -Path $TargetPath -Directory | Remove-Item -Recurse -Force
    Write-Host "清空插件目录: $TargetPath" -ForegroundColor Gray
}

Write-Host "完成" -ForegroundColor Green
Write-Host ""

# 7. 安装到 Flow Launcher
Write-Host "[7/8] 安装插件到 Flow Launcher..." -ForegroundColor Yellow

# 复制编译输出到目标目录
Get-ChildItem -Path $OutputPath | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $TargetPath -Recurse -Force
}

Write-Host "完成" -ForegroundColor Green
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "安装完成!" -ForegroundColor Green
Write-Host ""
Write-Host "插件目录: $PluginFolderName" -ForegroundColor Gray
Write-Host "插件位置: $TargetPath" -ForegroundColor Gray
Write-Host "编译配置: $Configuration" -ForegroundColor Gray
Write-Host ""

# 8. 重启 Flow Launcher
Write-Host "[8/8] 重启 Flow Launcher..." -ForegroundColor Yellow
$FlowLauncherExe = Join-Path $env:LOCALAPPDATA "FlowLauncher\Flow.Launcher.exe"
if (Test-Path $FlowLauncherExe) {
    Start-Process -FilePath $FlowLauncherExe
    Write-Host "已启动 Flow Launcher" -ForegroundColor Green
} else {
    Write-Host "警告: 未找到 Flow Launcher 可执行文件: $FlowLauncherExe" -ForegroundColor Red
}
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
