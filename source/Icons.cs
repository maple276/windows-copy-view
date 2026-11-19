using System.Drawing;
using System.Runtime.InteropServices;

namespace CopyHint;

/// <summary>
/// 托盘图标。
///
/// 两个要点（都是踩过的坑）：
/// 1. 托盘（Shell_NotifyIcon）要的是**图标管理器**管理的 HICON。
///    实测在这台机器上 CreateIconFromResourceEx 一律返回 NULL，
///    所以唯一可靠的路子是 LoadImage：从 .ico 文件里按托盘真实尺寸取那一帧。
/// 2. 素材 copy.ico 只有 256x256 一帧，没有托盘要的 16/20/24，
///    LoadImage 只能靠系统缩放，小尺寸会发虚。所以启动时把它重采样成
///    「16/20/24/32/48 多帧 ico」写到程序目录，并监听文件变化：
///    用户直接替换 copy.ico，程序会自动重新生成并刷新托盘图标。
/// </summary>
internal static class Icons
{
    const string SourceFile = "copy.ico";        // 素材（用户可替换）
    const string TrayFile = "copy-tray.ico";     // 自动生成的多尺寸版本

    const uint IMAGE_ICON = 1;
    const uint LR_LOADFROMFILE = 0x00000010;
    static readonly int[] Sizes = { 16, 20, 24, 32, 48, 64 };

    public static IntPtr App { get; private set; }

    /// <summary>素材 copy.ico 被替换时触发（在 FileSystemWatcher 线程上）。</summary>
    public static event Action? SourceChanged;

    static FileSystemWatcher? _watcher;

    public static void Load()
    {
        string sourcePath = Path.Combine(Program.AppDir(), SourceFile);
        string trayPath = Path.Combine(Program.AppDir(), TrayFile);

        // 1) 有素材就（必要时）生成多尺寸托盘图标
        if (File.Exists(sourcePath))
        {
            if (BuildTrayIco(sourcePath, trayPath))
                App = LoadTrayIcon(trayPath);

            if (App != IntPtr.Zero)
            {
                WatchSource(sourcePath);
                return;
            }
        }

        // 2) 退回：直接加载素材（系统自己缩放）
        if (File.Exists(sourcePath))
        {
            App = LoadTrayIcon(sourcePath);
            if (App != IntPtr.Zero)
            {
                Program.Log("tray icon: loaded source ico directly (no downscale)");
                WatchSource(sourcePath);
                return;
            }
        }

        // 3) 退到嵌入资源：先写成临时多尺寸 ico，再用 LoadImage 加载
        string tempPath = Path.Combine(Path.GetTempPath(), "CopyHint_tray.ico");
        if (ExtractEmbeddedIco(tempPath))
        {
            App = LoadTrayIcon(tempPath);
            if (App != IntPtr.Zero)
            {
                Program.Log("tray icon: loaded from embedded ico via temp file");
                return;
            }
        }

        // 4) 兜底：系统默认图标（只在前面全部失败时才会出现）
        App = LoadIconW(IntPtr.Zero, 32512 /* IDI_APPLICATION */);
        Program.Log("tray icon: fell back to IDI_APPLICATION");
    }

    /// <summary>按托盘实际尺寸加载。让托盘自己缩放也算可控，先取最大尺寸再交给它缩。</summary>
    public static IntPtr LoadTrayIcon(string icoPath)
    {
        int size = PreferredSize();
        IntPtr h = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);
        if (h != IntPtr.Zero)
        {
            Program.Log($"tray icon: {icoPath} @{size}px");
            return h;
        }
        Program.Log($"tray icon: LoadImage failed for {icoPath}, err={Marshal.GetLastWin32Error()}");

        // 这个尺寸在文件里没有时，再试系统默认尺寸那一帧
        h = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
        if (h != IntPtr.Zero) Program.Log($"tray icon: {icoPath} @default");
        return h;
    }

    /// <summary>托盘实际用多大就取多大：16px 主机取 16，125% 缩放的 20px 主机取 20，最多取 32。</summary>
    public static int PreferredSize()
    {
        int px = (int)Math.Round(16 * (Native.PrimaryDpiPercent() / 96.0));
        return Math.Clamp(px, 16, 32);
    }

    // ---------------- 多尺寸 ico 生成 ----------------

    static bool BuildTrayIco(string sourcePath, string trayPath)
    {
        try
        {
            if (File.Exists(trayPath))
            {
                // 已经比素材新就不用重建
                if (File.GetLastWriteTimeUtc(trayPath) >= File.GetLastWriteTimeUtc(sourcePath))
                    return true;
            }

            Bitmap? source = null;
            try
            {
                using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var icon = new Icon(fs, 256, 256);
                using var bmp = icon.ToBitmap();
                source = new Bitmap(bmp);
            }
            catch (Exception ex)
            {
                Program.Log("tray icon: cannot decode source ico: " + ex.Message);
                return false;
            }

            using (source)
            {
                byte[] bytes = PackIco(source, Sizes);
                // 先写临时文件再替换，避免用户正打开时写出半截文件
                string tmp = trayPath + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Copy(tmp, trayPath, true);
                File.Delete(tmp);
                Program.Log($"tray icon: rebuilt {trayPath} ({bytes.Length} bytes, {Sizes.Length} frames)");
                return true;
            }
        }
        catch (Exception ex)
        {
            Program.Log("tray icon: rebuild failed: " + ex.Message);
            return false;
        }
    }

    static bool ExtractEmbeddedIco(string outPath)
    {
        try
        {
            using var stream = typeof(Icons).Assembly.GetManifestResourceStream("CopyHint.copy.ico");
            if (stream == null) return false;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            using var icon = new Icon(new MemoryStream(ms.ToArray()), 256, 256);
            using var bmp = icon.ToBitmap();
            using var source = new Bitmap(bmp);
            File.WriteAllBytes(outPath, PackIco(source, Sizes));
            return true;
        }
        catch (Exception ex)
        {
            Program.Log("tray icon: embedded extraction failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>打成多帧 PNG 压缩 ico（Vista 以后都支持，体积小、缩放干净）。</summary>
    static byte[] PackIco(Bitmap source, int[] sizes)
    {
        var frames = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using var scaled = Resize(source, sizes[i]);
            frames[i] = Png(scaled);
        }

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write(frames[i].Length);
                bw.Write(offset);
                offset += frames[i].Length;
            }
            foreach (var f in frames) bw.Write(f);
        }
        return ms.ToArray();
    }

    static Bitmap Resize(Bitmap src, int size)
    {
        var dst = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.DrawImage(src, new Rectangle(0, 0, size, size));
        return dst;
    }

    static byte[] Png(Image img)
    {
        using var ms = new MemoryStream();
        img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    // ---------------- 监听素材变化 ----------------

    static void WatchSource(string sourcePath)
    {
        if (_watcher != null) return;
        try
        {
            string dir = Path.GetDirectoryName(sourcePath)!;
            string file = Path.GetFileName(sourcePath);
            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
            };
            _watcher.Changed += OnSourceTouched;
            _watcher.Created += OnSourceTouched;
            _watcher.Renamed += OnSourceTouched;
        }
        catch (Exception ex)
        {
            Program.Log("tray icon: watcher failed: " + ex.Message);
        }
    }

    static void OnSourceTouched(object sender, FileSystemEventArgs e)
    {
        // 文件可能还在写入，稍微等一下再交给 UI 线程处理
        Task.Delay(300).ContinueWith(_ =>
        {
            try { SourceChanged?.Invoke(); } catch { }
        });
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadImageW(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);
}
