# LinkDetect

LinkDetect 是一个 Windows 剪贴板链接检测工具。它常驻系统托盘，在复制的文本中发现 HTTP/HTTPS 链接后，以自绘悬浮卡片显示链接，不使用 Windows 通知中心，也不会自动超时关闭。

## 功能

- 使用 Windows 剪贴板更新消息实时监听，不轮询剪贴板。
- 从抖音、小红书等分享文案中提取全部 HTTP/HTTPS 链接并去重。
- 右下角显示可拖动、置顶的自绘悬浮卡片。
- 每个链接以独立的单行圆角条显示，域名和路径分层着色，悬停可查看完整地址；条目之间的背景透明。
- 使用随程序携带的 Maple Mono NF CN 字体，统一中英文及常规／中等字重；链接列表和设置使用细圆角滚动条。
- 点击链接后用默认浏览器打开，并从卡片中移除该链接。
- 新剪贴板内容会替换当前卡片；没有链接的内容会隐藏卡片。
- 托盘菜单支持暂停监听、开机自动运行和退出。
- 不联网解析、不记录剪贴板历史、不收集遥测。

## 运行要求

- Windows 10 或 Windows 11，x64。
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（运行 framework-dependent 发布版本时需要）。

## 开发与测试

字体资源与来源说明位于 [Assets/Fonts](src/LinkDetect/Assets/Fonts/README.md)，采用 SIL Open Font License 1.1。发布时会自动携带 Assets/Fonts/LICENSE.txt，不需要安装系统字体。

```powershell
dotnet restore LinkDetect.sln
dotnet test LinkDetect.sln
dotnet run --project .\src\LinkDetect\LinkDetect.csproj
```

程序启动后不会显示主窗口。请在系统托盘中找到 `LinkDetect` 图标。复制包含链接的文本即可显示悬浮卡片。

## 发布

生成依赖 .NET 8 Desktop Runtime 的 win-x64 单文件程序：

```powershell
dotnet publish .\src\LinkDetect\LinkDetect.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

输出目录默认为：

```text
src\LinkDetect\bin\Release\net8.0-windows\win-x64\publish
```

将发布目录中的文件复制到固定位置后运行 `LinkDetect.exe`。如果启用“开机自动运行”，移动程序前应先关闭该选项，移动完成后再重新启用。

## 操作说明

- 悬浮窗右上角圆环按钮一键关闭全部内容，也可按 Esc；不会退出程序。
- 按住圆环关闭按钮并拖动可以调整位置，拖动不会触发关闭。位置保存在 `%LocalAppData%\LinkDetect\settings.json`。
- 托盘中的“暂停监听”会隐藏当前卡片，并忽略后续剪贴板更新，直到恢复监听。
- 复制图片后，卡片会显示裁剪预览、处理状态和图片尺寸；裁剪完成后可点击“复制图片”。
- 托盘中的“设置”可调整图片裁剪阈值（50%–90%，默认 60%）。数值越高，裁剪越保守；保存后对下次复制的图片生效。“恢复默认”只调整当前编辑值，点击“保存”后才写入设置。
- “开机自动运行”默认关闭，只写入当前用户启动项，不需要管理员权限。
- 托盘中的“退出”会停止监听并完全退出程序。
