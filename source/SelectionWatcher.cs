using System.Runtime.InteropServices;

namespace CopyHint;

internal static class NativeExtra
{
    public const uint EVENT_SYSTEM_CAPTUREEND = 0x0009;
    public const int OBJID_CARET = -16;

    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    public static extern IntPtr GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>该线程里当前对话框要弹在哪个窗口（没有对话框时为 0），用来识别“另存为”等弹窗。</summary>
    public static IntPtr ForegroundDialogOwner(out int ownerStyle)
    {
        ownerStyle = 0;
        try
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return IntPtr.Zero;
            uint tid = Native.GetWindowThreadProcessId(fg, out _);
            if (tid == 0) return IntPtr.Zero;
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(tid, ref info) == IntPtr.Zero) return IntPtr.Zero;
            ownerStyle = (int)info.flags;
            return info.hwndActive;
        }
        catch { return IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public Native.RECT rcCaret;
    }
}

/// <summary>鼠标选区结束（鼠标松开）时的通知，用于给“Ctrl+C 其实可能没成功”补上确定性。</summary>
internal sealed class SelectionWatcher : IDisposable
{
    readonly NativeExtra.WinEventDelegate _cb;
    IntPtr _hook;

    public event Action? SelectionEnded;

    public SelectionWatcher()
    {
        _cb = OnEvent;
        _hook = NativeExtra.SetWinEventHook(NativeExtra.EVENT_SYSTEM_CAPTUREEND, NativeExtra.EVENT_SYSTEM_CAPTUREEND,
            IntPtr.Zero, _cb, 0, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
    }

    void OnEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != NativeExtra.OBJID_CARET) return;
        try { SelectionEnded?.Invoke(); } catch { }
    }

    /// <summary>取当前前台窗口那个线程里是否有可见文本光标（判断“有选区”）。</summary>
    public static bool ForegroundHasCaret()
    {
        try
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint pid;
            uint tid = GetWindowThreadProcessId(fg, out pid);
            if (tid == 0) return false;
            var info = new NativeExtra.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeExtra.GUITHREADINFO>() };
            if (NativeExtra.GetGUIThreadInfo(tid, ref info) == IntPtr.Zero) return false;
            return info.hwndCaret != IntPtr.Zero;
        }
        catch { return false; }
    }

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeExtra.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
            GC.KeepAlive(_cb);
        }
    }
}
