using System.Drawing;
using System.Text;

namespace CopyHint;

internal sealed class TrayAppContext : ApplicationContext
{
    const uint TRAY_ID = 1;
    const int WM_TRAYMSG = 0x8000 + 0x20;
    const int HMENU_TOGGLE = 1;
    const int HMENU_OPEN_DIR = 2;
    const int HMENU_EXIT = 3;

    const int TextMaxChars = 4 * 1024 * 1024;

    readonly MessageWindow _msg = new();
    readonly HintForm _hint = new();
    readonly SelectionWatcher _selection = new();
    readonly object _gate = new();

    bool _enabled = true;
#if TESTS
    readonly bool _debug = Environment.GetCommandLineArgs().Contains("-debug");
#else
    const bool _debug = false;
#endif
    uint _lastSeq;
    string _lastFingerprint = "";
    long _lastShownTick;
    long _lastSuccessTick;

    public TrayAppContext()
    {
        Icons.Load();
        LoadSettings();

        _msg.CopyUpdate += OnCopyUpdate;
        _msg.TrayClicked += OnTrayClicked;
        AddTray(Native.NIM_ADD);
        _selection.SelectionEnded += OnSelectionEnded;

        // 缩放比例一改（换显示器、改显示设置），按旧尺寸做的图标就不再匹配，
        // 托盘会悄悄退回默认图标。所以 DPI 变化时按新尺寸重建一张并刷新托盘。
        _msg.DpiChanged += RefreshTrayIcon;

        // 用户直接替换了 copy.ico：重新生成多尺寸版本并刷新托盘
        Icons.SourceChanged += () => _msg.Post(RefreshTrayIcon);

        // 启动时先记下当前序号，避免把“旧剪贴板”当成一次新复制来提示
        _lastSeq = Native.GetClipboardSequenceNumber();
        if (!Native.AddClipboardFormatListener(_msg.Handle))
            Program.Log("AddClipboardFormatListener failed: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    /// <summary>系统 DPI 变了：重建图标后 NIM_MODIFY 刷新托盘上的那一张。</summary>
    void RefreshTrayIcon()
    {
        try
        {
            Icons.Load();
            AddTray(Native.NIM_MODIFY);
            Program.Log("tray icon refreshed after dpi change: " + Native.PrimaryDpiPercent() + "%");
        }
        catch (Exception ex)
        {
            Program.Log("RefreshTrayIcon failed: " + ex.Message);
        }
    }

    // ---------------- 托盘 ----------------

    void AddTray(uint action)
    {
        var nid = new Native.NOTIFYICONDATAW
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.NOTIFYICONDATAW>(),
            hWnd = _msg.Handle,
            uID = TRAY_ID,
            uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP,
            uCallbackMessage = WM_TRAYMSG,
            hIcon = Icons.App,
            szTip = "复制提示 · 文本 + 文件（单击/右键打开菜单）",
            szInfo = "",
            szInfoTitle = "",
        };
        Native.Shell_NotifyIconW(action, ref nid);
    }

    void OnTrayClicked()
    {
        Native.GetCursorPos(out var p);
        ShowMenu(p);
    }

    void ShowMenu(Native.POINT p)
    {
        IntPtr menu = Native.CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            Native.AppendMenuW(menu, 0x00000000u | (_enabled ? 0x00000008u : 0u), (IntPtr)HMENU_TOGGLE, "启用复制提示");
            Native.AppendMenuW(menu, 0x00000000, (IntPtr)HMENU_OPEN_DIR, "打开程序所在文件夹");
            Native.AppendMenuW(menu, 0x00000800, IntPtr.Zero, null);
            Native.AppendMenuW(menu, 0x00000000, (IntPtr)HMENU_EXIT, "退出");

            Native.SetForegroundWindow(_msg.Handle);
            int cmd = Native.TrackPopupMenu(menu,
                Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD | Native.TPM_NONOTIFY,
                p.X, p.Y, 0, _msg.Handle, IntPtr.Zero);
            switch (cmd)
            {
                case HMENU_TOGGLE:
                    _enabled = !_enabled;
                    SaveSettings();
                    Balloon(_enabled ? "复制提示已开启" : "复制提示已暂停");
                    break;
                case HMENU_OPEN_DIR:
                    try { System.Diagnostics.Process.Start("explorer.exe", Program.AppDir()); } catch { }
                    break;
                case HMENU_EXIT:
                    ExitThread();
                    break;
            }
        }
        finally
        {
            Native.DestroyMenu(menu);
        }
    }

    void Balloon(string text)
    {
        try
        {
            var nid = new Native.NOTIFYICONDATAW
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.NOTIFYICONDATAW>(),
                hWnd = _msg.Handle,
                uID = TRAY_ID,
                uFlags = Native.NIF_INFO,
                szInfo = text,
                szInfoTitle = "复制提示",
                dwInfoFlags = Native.NIIF_INFO,
            };
            Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref nid);
        }
        catch { }
    }

    // ---------------- 剪贴板 ----------------

    void OnCopyUpdate()
    {
        if (_debug) Program.Log($"[debug] WM_CLIPBOARDUPDATE seq={Native.GetClipboardSequenceNumber()} enabled={_enabled}");
        if (!_enabled) return;

        uint seq = Native.GetClipboardSequenceNumber();
        lock (_gate)
        {
            if (seq == 0 || seq == _lastSeq) return;
            _lastSeq = seq;
        }

        // 一次复制会连发 2~3 条通知，只处理第一条；读剪贴板可能被短暂占用，放到后台线程避免卡住消息循环
        Task.Run(() => HandleCopy());
    }

    void HandleCopy()
    {
        CopyEvent? ev = null;
        try
        {
            // 每次复制往往连发 2 次 WM_CLIPBOARDUPDATE（先清空几个格式，再写入内容），
            // 这里等每次变化都落定后再读，避免读到“半成品”剪贴板。
            bool ready = false;
            for (int i = 0; i < 8; i++)
            {
                if (Native.IsClipboardFormatAvailable(Native.CF_HDROP) ||
                    Native.IsClipboardFormatAvailable(Native.CF_UNICODETEXT))
                {
                    ready = true;
                    break;
                }
                Thread.Sleep(10);
            }
            if (!ready) return;

            if (Native.IsClipboardFormatAvailable(Native.CF_HDROP))
            {
                string[] files = ClipboardFiles.GetFiles();
                if (files.Length > 0) ev = CopyEvent.ForFiles(files);
            }

            if (ev == null && Native.IsClipboardFormatAvailable(Native.CF_UNICODETEXT))
            {
                string? text = ReadText();
                if (!string.IsNullOrEmpty(text)) ev = CopyEvent.ForText(text);
            }
        }
        catch (Exception ex)
        {
            Program.Log("HandleCopy: " + ex);
            return;
        }

        if (ev == null) return;

        // 连按两下 Ctrl+C / 同一内容在 2.5 秒内复制两次：只提示一次
        long now = Environment.TickCount64;
        bool repeated;
        lock (_gate)
        {
            repeated = ev.Fingerprint == _lastFingerprint && now - _lastShownTick < 2500;
            if (!repeated) _lastFingerprint = ev.Fingerprint;
            _lastShownTick = Math.Max(_lastShownTick, now); // 记录最后活动时间
            _lastSuccessTick = now;
        }
        if (_debug)
            Program.Log($"copy seq={Native.GetClipboardSequenceNumber()} kind={(ev.IsFile ? "file" : "text")} title='{ev.Title}' repeated={repeated}");
        if (repeated) return;

        ShowOnUi(ev);
    }

    void ShowOnUi(CopyEvent ev)
    {
        try
        {
            _msg.Post(() =>
            {
                if (!_enabled) return;
                try { _hint.ShowEvent(ev, ev.Sticky ? 2400 : 1150); }
                catch (Exception ex) { Program.Log("ShowEvent: " + ex); }
                if (_debug) Program.Log($"shown '{ev.Title}'");
            });
        }
        catch (Exception ex) { Program.Log("BeginInvoke: " + ex); }
    }

    /// <summary>
    /// 兜底：鼠标选区刚结束（松开鼠标）但 420ms 内剪贴板没有更新 ——
    /// 说明“明明选中了、Ctrl+C 却没生效”，这多半就是快捷键被抢/被软件拦了，给一个提醒。
    /// （另存为、关闭等对话框弹出时不算，避免干扰。）
    /// </summary>
    void OnSelectionEnded()
    {
        if (!_enabled) return;

        long fired = Environment.TickCount64;
        Task.Delay(420).ContinueWith(ignore =>
        {
            try
            {
                long now = Environment.TickCount64;
                lock (_gate)
                {
                    if (_lastSuccessTick >= fired) return;   // 期间有成功复制 → 已提示成功
                    if (now - _lastShownTick < 1500) return; // 刚提示过（可能是别处复制），别打扰
                }

                // 确认仍是“有选区的编辑/浏览窗口”，而不是弹出了对话框
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero) return;
                bool caret = SelectionWatcher.ForegroundHasCaret();
                IntPtr active = NativeExtra.ForegroundDialogOwner(out int flags);
                bool dialogOpen = active != IntPtr.Zero && (active == fg || (flags & 0x0001 /* GUI_INMENUMODE */) != 0 || (flags & 0x0004 /* GUI_POPUPMENUMODE */) != 0);
                if (dialogOpen) return;
                if (!caret) return;

                lock (_gate) { _lastShownTick = now; }
                ShowOnUi(new CopyEvent
                {
                    Title = "有选区，但没进剪贴板",
                    Detail = "复制可能失败了，可再按一次 Ctrl+C",
                    Warning = true,
                    Fingerprint = "W:" + now,
                    Sticky = true,
                });
            }
            catch { }
        });
    }

    /// <summary>用 Win32 直接读 CF_UNICODETEXT，比 WinForms Clipboard 更适合和 WPS 并存。</summary>
    static string? ReadText()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (!Native.OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(20); // 剪贴板正被别的程序占用
                continue;
            }
            try
            {
                IntPtr h = Native.GetClipboardData(Native.CF_UNICODETEXT);
                if (h == IntPtr.Zero) return null;
                IntPtr p = Native.GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try
                {
                    string s = System.Runtime.InteropServices.Marshal.PtrToStringUni(p) ?? "";
                    if (s.Length > TextMaxChars) s = s[..TextMaxChars];
                    return s;
                }
                finally { Native.GlobalUnlock(h); }
            }
            catch { return null; }
            finally { Native.CloseClipboard(); }
        }
        return null;
    }

    // ---------------- 设置 ----------------

    static string SettingsPath => Path.Combine(Program.AppDir(), "copyhint.ini");

    void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line[..eq].Trim();
                string v = line[(eq + 1)..].Trim();
                if (k.Equals("enabled", StringComparison.OrdinalIgnoreCase)) _enabled = v != "0";
            }
        }
        catch { }
    }

    void SaveSettings()
    {
        try { File.WriteAllText(SettingsPath, "enabled=" + (_enabled ? "1" : "0") + Environment.NewLine); }
        catch { }
    }

    // ---------------- 退出 ----------------

    protected override void ExitThreadCore()
    {
        try
        {
            Native.RemoveClipboardFormatListener(_msg.Handle);
            AddTray(Native.NIM_DELETE);
        }
        catch { }
        try { _selection.Dispose(); } catch { }
        try { _hint.Dispose(); } catch { }
        try { _msg.DestroyHandle(); } catch { }
        base.ExitThreadCore();
    }
}

/// <summary>隐藏消息窗口：接收剪贴板更新和托盘回调，并作为 UI 线程的调度点。</summary>
internal sealed class MessageWindow : NativeWindow
{
    const int WM_FORWARDMSG = 0x8000 + 0x23;
    static readonly int s_posted = RegisterWindowMessage("CopyHint.PostedAction");

    readonly System.Collections.Concurrent.ConcurrentQueue<Action> _queue = new();

    public event Action? CopyUpdate;
    public event Action? TrayClicked;
    public event Action? DpiChanged;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int RegisterWindowMessageW(string lpString);

    static int RegisterWindowMessage(string name) => RegisterWindowMessageW(name);

    public MessageWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "CopyHint.MessageWindow",
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
        });
    }

    /// <summary>把一段代码排到创建本窗口的线程（即 UI 线程）上执行。</summary>
    public void Post(Action action)
    {
        _queue.Enqueue(action);
        Native.PostMessageW(Handle, (uint)s_posted, IntPtr.Zero, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_TRAYMSG = 0x8000 + 0x20;
        const int WM_LBUTTONUP = 0x0202;
        const int WM_RBUTTONUP = 0x0205;
        _ = WM_FORWARDMSG;

        if (m.Msg == Native.WM_CLIPBOARDUPDATE)
        {
            try { CopyUpdate?.Invoke(); } catch { }
            return;
        }

        if (m.Msg == Native.WM_TRAY_CLICKED)
        {
            try { TrayClicked?.Invoke(); } catch { }
            return;
        }

        if (m.Msg == Native.WM_DPICHANGED)
        {
            try { DpiChanged?.Invoke(); } catch { }
            return;
        }

        if (m.Msg == s_posted)
        {
            while (_queue.TryDequeue(out var a))
            {
                try { a(); } catch (Exception ex) { Program.Log("Posted action: " + ex); }
            }
            return;
        }

        if (m.Msg == WM_TRAYMSG)
        {
            int evt = (int)(m.LParam.ToInt64() & 0xFFFF);
            if (evt is WM_LBUTTONUP or WM_RBUTTONUP)
            {
                try { TrayClicked?.Invoke(); } catch { }
            }
            return;
        }

        base.WndProc(ref m);
    }
}
