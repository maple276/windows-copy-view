using System.Runtime.InteropServices;

namespace CopyHint;

internal static class ExplorerHeuristics
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    static extern IntPtr GetFocus();

    static bool IsListClass(string cls) =>
        cls.Equals("SysListView32", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 光标或前台窗口位于资源管理器的文件列表 / Everything 列表上时返回 true。
    /// 这是小概率兜底，只用于“文本里只有一个路径”时算不算文件复制。
    /// </summary>
    public static bool IsFileManagerContext()
    {
        try
        {
            Native.GetCursorPos(out var p);
            IntPtr under = Native.WindowFromPoint(p);
            if (InList(under)) return true;
            if (InList(Native.GetForegroundWindow())) return true;
            if (InList(GetFocus())) return true;
            return IsEverything(Native.GetForegroundWindow());
        }
        catch { return false; }
    }

    static bool InList(IntPtr h)
    {
        if (h == IntPtr.Zero) return false;
        for (int i = 0; i < 8 && h != IntPtr.Zero; i++)
        {
            var sb = new System.Text.StringBuilder(256);
            Native.GetClassName(h, sb, sb.Capacity);
            if (IsListClass(sb.ToString())) return true;
            h = Native.GetAncestor(h, 1 /* GA_PARENT */);
        }
        return false;
    }

    static bool IsEverything(IntPtr h)
    {
        try
        {
            if (h == IntPtr.Zero) return false;
            uint tid = GetWindowThreadProcessId(h, out uint pid);
            _ = tid;
            if (pid == 0) return false;
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return proc.ProcessName.Contains("Everything", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
