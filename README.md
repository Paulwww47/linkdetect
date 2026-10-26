# LinkDetect

LinkDetect 是一个 Windows 剪贴板链接检测工具。它常驻系统托盘，在复制的文本中发现 HTTP/HTTPS 链接后，以自绘悬浮卡片显示链接，不使用 Windows 通知中心，也不会自动超时关闭。

## 功能

- 使用 Windows 剪贴板更新消息实时监听，不轮询剪贴板。
- 从抖音、小红书等分享文案中提取全部 HTTP/HTTPS 链接并去重。
- 复制纯文字时卡片同样保留，右上角显示“分词”小按钮：按词切分后在卡片内以词块显示，可点选或按住拖动连续选词（跨行拖动会选中中间整段），选中内容按原文顺序写回剪贴板。
- 词块支持鼠标中键继续拆分（较长的词拆成更短的词，直到单字、单字母或单个 emoji）。
- 右下角显示可拖动、置顶的自绘悬浮卡片。
- 每个链接以独立的单行圆角条显示，域名和路径分层着色，悬停可查看完整地址；条目之间的背景透明。
- 使用随程序携带的 Maple Mono NF CN 字体，统一中英文及常规／中等字重；链接列表和设置使用细圆角滚动条。
- 点击链接后用默认浏览器打开，并从卡片中移除该链接。
- 新剪贴板内容会替换当前卡片；纯空白内容会隐藏卡片。
- 托盘菜单支持暂停监听、开机自动运行和退出。
- 不联网解析、不记录剪贴板历史、不收集遥测。

## 运行要求

- Windows 10 或 Windows 11，x64。
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（运行 framework-dependent 发布版本时需要）。

## 开发

字体资源与来源说明位于 [Assets/Fonts](src/LinkDetect/Assets/Fonts/README.md)，采用 SIL Open Font License 1.1。发布时会自动携带 Assets/Fonts/LICENSE.txt，不需要安装系统字体。

中文分词使用 jieba.NET（MIT）。词典与模型文件位于 [Assets/Jieba](src/LinkDetect/Assets/Jieba/LICENSE.txt)，内嵌在程序集中，首次分词时释放到 `%LocalAppData%\LinkDetect\jieba`，因此单文件发布不需要额外携带词典文件。

```powershell
dotnet restore LinkDetect.sln
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
- 复制文字后点卡片右上角的“分词”，文本会按词切分成词块：单击选中或取消，按住左键拖动可连续刷选（跨行拖动会选中中间整段，而不只是鼠标划过的词块），中键点击词块可继续拆分。分词后右上角出现“复制 N”，点击后把选中的词按原文顺序拼接写入剪贴板（原文相邻的词块直接相连，中间有标点或空格则用一个空格分隔）。
- 分词上限为 1000 字；超过上限时“分词”按钮置灰并提示文本过长，链接识别仍然可用。
- 托盘中的“设置”可调整图片裁剪阈值（50%–90%，默认 60%）。数值越高，裁剪越保守；保存后对下次复制的图片生效。“恢复默认”只调整当前编辑值，点击“保存”后才写入设置。
- “开机自动运行”默认关闭，只写入当前用户启动项，不需要管理员权限。
- 托盘中的“退出”会停止监听并完全退出程序。
