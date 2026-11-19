using System.Runtime.InteropServices;

namespace CopyHint;

internal static class Native
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_TRAY_CLICKED = 0x8000 + 0x21; // 自定义：第二个实例启动时用来唤起菜单

    public const uint CF_UNICODETEXT = 13;
    public const uint CF_HDROP = 15;

    // SetWindowPos
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    // TrackPopupMenu
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;

    // Shell_NotifyIcon
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_INFO = 0x00000010;
    public const uint NIIF_INFO = 0x00000001;

    // GlobalAlloc
    public const uint GMEM_MOVEABLE = 0x0002;

    public const int MONITOR_DEFAULTTONEAREST = 2;

    public const uint LVM_GETITEMCOUNT = 0x1004;
    public const uint LVM_GETNEXTITEM = 0x100C;
    public const uint LVNI_SELECTED = 0x0002;
    public const uint CDDS_ITEMPOSTPAINT = 0x00010000 | 0x00000001;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, char[]? lpszFile, uint cch);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    /// <summary>把一个 .lnk 的路径以 CF_HDROP 放进剪贴板（不新建文件，直接喂 DROPFILES 结构）。</summary>
    public static bool PutFileInClipboard(string path)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            int chars = path.Length + 2; // 结尾两个 \0
            int header = 20;             // DROPFILES 结构大小
            int bytes = header + chars * 2;
            IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (h == IntPtr.Zero) return false;
            IntPtr p = GlobalLock(h);
            if (p == IntPtr.Zero) { GlobalFree(h); return false; }
            try
            {
                var buf = new byte[bytes];
                BitConverter.GetBytes(header).CopyTo(buf, 0);   // pFiles
                BitConverter.GetBytes(0).CopyTo(buf, 4);         // pt.x
                BitConverter.GetBytes(0).CopyTo(buf, 8);         // pt.y
                BitConverter.GetBytes(0).CopyTo(buf, 12);        // fNC
                BitConverter.GetBytes(1).CopyTo(buf, 16);        // fWide = TRUE
                System.Text.Encoding.Unicode.GetBytes(path + "\0\0").CopyTo(buf, header);
                Marshal.Copy(buf, 0, p, bytes);
            }
            finally { GlobalUnlock(h); }

            if (SetClipboardData(CF_HDROP, h) == IntPtr.Zero) { GlobalFree(h); return false; }
            return true; // 设置成功后所有权归系统，不能再 GlobalFree
        }
        catch { return false; }
        finally { CloseClipboard(); }
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int w, int h, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    // ---------- 托盘 ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Gdi32.POINT pptDst,
        ref Gdi32.SIZE psize, IntPtr hdcSrc, ref Gdi32.POINT pptSrc, int crKey,
        ref Gdi32.BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    public static uint MF_STRING => 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_CHECKED = 0x00000008;

    /// <summary>取主显示器 DPI 缩放百分比（96 = 100%）。</summary>
    public static int PrimaryDpiPercent()
    {
        try
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return 96;
            int dpi = GetDeviceCaps(hdc, 88 /* LOGPIXELSX */);
            ReleaseDC(IntPtr.Zero, hdc);
            return dpi is > 48 and < 960 ? dpi : 96;
        }
        catch { return 96; }
    }
}

/// <summary>从剪贴板读取文件列表（CF_HDROP）。</summary>
internal static class ClipboardFiles
{
    public static string[] GetFiles()
    {
        if (!Native.OpenClipboard(IntPtr.Zero)) return Array.Empty<string>();
        try
        {
            IntPtr h = Native.GetClipboardData(Native.CF_HDROP);
            if (h == IntPtr.Zero) return Array.Empty<string>();
            uint count = Native.DragQueryFileW(h, 0xFFFFFFFF, null, 0);
            if (count == 0) return Array.Empty<string>();
            var list = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                uint len = Native.DragQueryFileW(h, i, null, 0);
                if (len == 0) continue;
                var buf = new char[len + 1];
                uint got = Native.DragQueryFileW(h, i, buf, len + 1);
                if (got > 0) list.Add(new string(buf, 0, (int)got));
            }
            return list.ToArray();
        }
        catch { return Array.Empty<string>(); }
        finally { Native.CloseClipboard(); }
    }
}

/// <summary>判断单段文本是不是“路径”，用于资源管理器/Everything 里 Ctrl+C 的兜底识别。</summary>
internal static class PathHeuristics
{
    public static bool LooksLikePath(string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim().Trim('"');
            if (s.Length < 3 || s.Length > 32767) return false;
            if (s.Contains('\n') || s.Contains('\r')) return false;
            if (File.Exists(s) || Directory.Exists(s)) return true;
            if (Path.IsPathFullyQualified(s))
            {
                var fi = new FileInfo(s);
                if (!string.IsNullOrEmpty(fi.Name) && fi.Name.Contains('.')) return true;
            }
        }
        catch { }
        return false;
    }
}
