using System.Drawing;

namespace CopyHint;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        var args = Environment.GetCommandLineArgs();

        // -snap <out.png>：把三种卡片渲染成图片，方便在没有交互桌面的机器上检查外观
        if (args.Length > 2 && args[1] == "-snap")
        {
            Snapshot(args[2]);
            return;
        }

        // -test-window [nocopy]：仅测试用，单独启动一个占前台的窗口（模拟拖选 + Ctrl+C）
#if TESTS
        if (args.Length > 1 && args[1] == "-test-window")
        {
            ApplicationConfiguration.Initialize();
            TestWindow.Run(args.Length > 2 ? args[2] : "copy");
            return;
        }
#endif

        // 单实例
        using var mutex = new Mutex(true, @"Global\CopyHint.SingleInstance", out bool isNew);
        if (!isNew)
        {
            TryWakeExisting();
            return;
        }

        ApplicationConfiguration.Initialize();
        ToolTipIconHelper.EnableModernTooltips();

        // -hint 指纹 kind 标题 副标题 [毫秒]：仅供测试脚本使用，直接弹一个气泡
        if (args.Length >= 6 && args[1] == "-hint")
        {
            using var testForm = new HintForm();
            int ms = args.Length > 6 && int.TryParse(args[6], out int m) ? m : 2600;
            testForm.ErrorToLog = true;
            testForm.ShowEvent(new CopyEvent
            {
                IsFile = args[3] == "file",
                Warning = args[3] == "warn",
                Title = args[4],
                Detail = args[5],
            }, holdMs: ms);
            Application.Run();
            return;
        }

        Application.ThreadException += (_, e) => Log("ThreadException: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("Unhandled: " + e.ExceptionObject);

        try
        {
            Application.Run(new TrayAppContext());
        }
        catch (Exception ex)
        {
            Log("Fatal: " + ex);
            MessageBox.Show("复制提示启动失败：\n\n" + ex, "复制提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void Snapshot(string outPath)
    {
        var cards = new[]
        {
            CopyEvent.ForText("这是一段被复制的文字内容，用来验证预览效果"),
            CopyEvent.ForFiles(new[] { @"C:\Users\Administrator\Documents\季度汇报.pptx" }),
            new CopyEvent
            {
                Title = "有选区，但没进剪贴板",
                Detail = "复制可能失败了，可再按一次 Ctrl+C",
                Warning = true,
            },
        };

        using var sheet = new Bitmap(620 + 20, 3 * 72, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(245, 246, 248));
            for (int i = 0; i < cards.Length; i++)
            {
                using var bmp = CardPainter.Render(cards[i], 600, 64);
                g.DrawImage(bmp, 10, 12 + i * 72, 600, 64);
            }
        }
        sheet.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        Log("snapshot -> " + outPath);
    }

    /// <summary>已经有一个实例在跑：给它发消息，让它把菜单弹出来。</summary>
    internal static void TryWakeExisting()
    {
        for (int i = 0; i < 20; i++)
        {
            IntPtr h = FindWindowW(null, "CopyHint.MessageWindow");
            if (h != IntPtr.Zero)
            {
                Native.PostMessageW(h, Native.WM_TRAY_CLICKED, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            Thread.Sleep(100);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    internal static void Log(string message)
    {
        try
        {
            string dir = AppDir();
            File.AppendAllText(Path.Combine(dir, "copyhint.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    internal static string AppDir()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe)) return Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
            return AppContext.BaseDirectory;
        }
        catch { return Environment.CurrentDirectory; }
    }
}
