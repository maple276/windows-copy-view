# 复制提示 Pro

复制成功后在屏幕**右上角**弹一个长条气泡：**文本复制和文件复制都会提示**。

## 灵感来源

这个软件是照着 B 站上的一个视频做的：

**参考视频：<https://www.bilibili.com/video/BV194456NEzq>**

视频里的那个「复制提示」小工具，复制成功后会弹一个小反馈，解决「按了 Ctrl+C 却不知道有没有复制上」的烦躁感。
但它的局限是**只能识别文本复制**——在资源管理器 / WPS 里复制**文件**时没有任何提示，而复制文件恰恰是最容易失败、最需要确认的场景。

所以就有了这个扩展版：在保留文本复制反馈的基础上，把**文件复制**也纳入识别，
并额外处理「选区明明选上了、Ctrl+C 却没进剪贴板」的情况。原版程序就是桌面上那个「复制提示.exe」。

## 怎么用

1. 双击 `复制提示Pro.exe`（就是编译好的单文件版，约 49 MB，不依赖 .NET 运行时）。
2. 启动后只有一个托盘图标，没有窗口。
3. 复制任何东西试试：
   - 选中文字 `Ctrl+C` → 绿色气泡「已复制 N 个字符」+ 内容预览
   - 资源管理器里 `Ctrl+C` 一个文件 → 琥珀色气泡，显示文件名
   - 多选文件 → 「已复制 N 个文件」+ 所在目录
   - WPS / 网页 / 编辑器里复制的文字，同样提示
4. 托盘图标**单击或右键** → 菜单：启用/暂停、打开程序所在文件夹、退出。
5. 开机自启动：按 `win+R`，输入 `shell:startup`打开开机启动文件夹，将exe快捷方式拖入即可。

程序目录下会自动生成 `copyhint.ini` 记录「启用/暂停」状态（暂停状态重启后保留）。

## 气泡外观

- 位置：屏幕**右上角**，距右边、上边各 20 像素。
- 尺寸：宽 600、高 64（原来 300，现在加长一倍，路径能放得下）。
- 不抢焦点、鼠标穿透、1.15 秒后淡出（红色提醒停留 2.4 秒）。
- 三种配色：文本 = 薄荷绿，文件 = 琥珀色，复制失败提醒 = 珊瑚红。

## 识别规则

| 剪贴板里有什么 | 提示 |
| --- | --- |
| `CF_HDROP`（真正的文件列表） | 文件气泡 |
| 文本，且整段就是一个存在的文件/文件夹路径 | 文件气泡（兜底：某些程序只给路径文本） |
| 其它文本 | 文本气泡，显示字数 + 前 40 个字预览 |
| 有选区、松开鼠标后 420ms 内剪贴板没变化 | 红色提醒「有选区，但没进剪贴板」 |

细节：

- 一次复制会连发 2~3 条剪贴板通知，程序只处理第一条；同一内容 2.5 秒内重复复制只提示一次。
- 启动时不会把剪贴板里已有的旧内容弹出来。
- 弹出「另存为 / 打开 / 关闭」这类对话框时，不会误报「复制失败」。
- 单实例：重复双击不会开第二个，只会把菜单弹出来。

## 关于「复制失败」提醒

Win32 剪贴板本身没有「复制失败」的事件，所以这个提醒是推断出来的：
检测到「鼠标拖选刚结束（有可见文本光标）」且「之后剪贴板没有被更新」。
它不是 100% 准确，属于锦上添花；而且**它只能识别鼠标选区**——用键盘 `Shift+方向键` 选中的文本不会触发这个提醒。

## 已知限制

- **缩略图视图**：系统只在「详细信息 / 列表 / 平铺」视图下给剪贴板提供文件；缩略图（大图标）视图下资源管理器可能根本不往剪贴板放文件，这种情况下没有任何程序能提示。
- **虚拟文件**（压缩包里的内容、某些网盘/搜索软件的条目）不是真实文件，剪贴板里没有文件列表，会按文本处理。
- 权限更高的程序（以管理员运行的 WPS/资源管理器）使用剪贴板时，普通权限的进程可能读不到，会静默跳过。

## 目录结构与重新编译

```
copy-view/
  copy.ico              程序图标（exe 图标 + 托盘图标都用它）
  复制提示Pro.exe        编译好的单文件版（构建产物，GitHub 上放 Releases，不进仓库）
  preview.png           三种气泡样式预览图
  README.md             本文件
  .gitignore            忽略构建产物
  source/               源码（C# / WinForms / .NET 9，约 75 KB）
    Program.cs          入口、日志、单实例
    TrayAppContext.cs   托盘、剪贴板监听、启停设置、失败提醒
    Card.cs             气泡的绘制（文本/文件/提醒三种样式）
    HintForm.cs         分层窗口：位置、圆角、半透明、淡入淡出、不抢焦点
    SelectionWatcher.cs 鼠标选区结束监听、前台是否有文本光标
    Native.cs           剪贴板 / 托盘 / 显示器相关 P/Invoke
    Icons.cs            托盘图标：生成多尺寸 ico 并用 LoadImage 加载
    ToolTipIconHelper.cs Win11 新版气泡外观（失败不影响功能）
    TestWindow.cs / TestActions.cs  仅测试用的脚手架
```

运行时会在程序目录自动生成 `copy-tray.ico`（多尺寸托盘图标）、`copyhint.ini`（启停状态）、
`copyhint.log`（仅 `-debug` 时），这些都在 `.gitignore` 里。

### 编译

需要 .NET 9 SDK。**不要在仓库里留构建目录**——`bin/`、`obj/` 会膨胀到 300 MB 以上，且都能重新生成：

```powershell
dotnet build   copy-view\source\CopyHint.csproj -c Release -o copy-view\source\build
dotnet publish copy-view\source\CopyHint.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:PublishReadyToRun=true `
  -o copy-view\source\publish
```

`-p:PublishReadyToRun=true` 会让体积/编译时间大幅上升（中间产物里有一个 62 MB 的 R2R 缓存）。
只想要能跑的小一点版本，去掉这个参数即可（启动稍慢一点点）。

### 上传到 GitHub

仓库里实际只需要提交 **16 个文件、约 340 KB**：

```
.gitignore  copy.ico  preview.png  README.md  source\*.cs  source\CopyHint.csproj
```

`复制提示Pro.exe`（49 MB）属于构建产物，建议走 **GitHub Releases** 上传附件，别提交进仓库
（仓库 .gitignore 里已经把 `*.exe` 排除了）。想提交进仓库的话，把 `.gitignore` 里的 `*.exe` 一行删掉，
但要注意 GitHub 单文件限制 100 MB、仓库建议不超过 1 GB。

## 图标说明

- **exe 图标**：由 csproj 的 `ApplicationIcon` 直接嵌入 exe（文件、任务管理器、快捷方式显示的它）。
- **托盘图标**：不能用 GDI+ 现做的 HICON，实测这台机器上 `CreateIconFromResourceEx` 一律失败，
  只能用 `LoadImage` 从 .ico 文件里按托盘尺寸取帧。
- 又因为 `copy.ico` 只有一帧 256×256，托盘要的 16/20/24 那帧不存在会发虚，
  所以程序启动时会**自动生成 `copy-tray.ico`**（多尺寸重采样版）给托盘用。
- 换图标：直接替换 `copy-view\copy.ico` 即可，程序会**自动**重新生成、刷新托盘图标
  （监听文件变化，不用重启）；exe 自身的图标则需要重新编译/发布才会变。
- 缩放比例变化（换显示器、改显示设置）时也会自动按新尺寸重建托盘图标。

调试日志：用 `复制提示Pro.exe -debug` 启动，会在程序目录写 `copyhint.log`
（记录每次剪贴板事件、识别结果、是否去重）；排查「没提示 / 提示错了」时把日志发出来最快。
