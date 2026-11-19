using System.Drawing;
using System.Runtime.InteropServices;

namespace CopyHint;

/// <summary>自动化测试用的小工具：模拟拖选 → 松开鼠标 → Ctrl+C。</summary>
internal static class TestActions
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

    const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
    const byte VK_CONTROL = 0x11, VK_C = 0x43;
    const uint KEYUP = 0x0002;

    public static void DragSelectAndCopy(IntPtr hwnd, Rectangle r)
    {
        try
        {
            if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
            Thread.Sleep(150);
            Point a = new(r.Left + r.Width / 6, r.Top + r.Height / 2);
            Point b = new(r.Left + r.Width * 2 / 3, r.Top + r.Height / 2);

            SetCursorPos(a.X, a.Y); Thread.Sleep(40);
            mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(60);
            for (int i = 1; i <= 10; i++)
            {
                SetCursorPos(a.X + (b.X - a.X) * i / 10, a.Y);
                Thread.Sleep(20);
            }
            Thread.Sleep(80);
            mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);  // 松开鼠标 → 触发选区结束事件
            Thread.Sleep(100);

            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            Thread.Sleep(40);
            keybd_event(VK_C, 0, KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYUP, UIntPtr.Zero);
            Program.Log("test: drag+ctrlC done");
        }
        catch (Exception ex) { Program.Log("test: drag failed " + ex.Message); }
    }

    /// <summary>只拖选并松开鼠标，不按 Ctrl+C（用来验证“复制失败”提醒）。</summary>
    public static void DragSelectOnly(IntPtr hwnd, Rectangle r)
    {
        try
        {
            if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
            Thread.Sleep(150);
            Point a = new(r.Left + r.Width / 6, r.Top + r.Height / 2);
            Point b = new(r.Left + r.Width * 2 / 3, r.Top + r.Height / 2);
            SetCursorPos(a.X, a.Y); Thread.Sleep(40);
            mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(60);
            for (int i = 1; i <= 10; i++)
            {
                SetCursorPos(a.X + (b.X - a.X) * i / 10, a.Y);
                Thread.Sleep(20);
            }
            mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Program.Log("test: drag only done");
        }
        catch (Exception ex) { Program.Log("test: drag failed " + ex.Message); }
    }
}
