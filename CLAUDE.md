# CLAUDE.md

## 插件SDK开发文档
https://www.flowlauncher.com/docs/#/develop-dotnet-plugins

## 开发注意事项

1. **版本同步**: 修改功能后需要同步更新 [plugin.json](plugin.json) 中的 `Version` 字段和 [README.md](README.md)

3. **图标资源**: 图标放在 [Images/](Images/) 目录,构建时复制到输出目录

4. **国际化**: 字符串资源在 [Properties/Resources.resx](Properties/Resources.resx),使用 `IPluginI18n` 接口支持语言切换

7. **警告即错误**: 项目配置 `TreatWarningsAsErrors=true`,代码必须无警告才能编译
