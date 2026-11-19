using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CopyHint;

/// <summary>
/// 一个置顶、不抢焦点的圆角小气泡。
/// 用分层窗口（WS_EX_LAYERED + UpdateLayeredWindow），半透明圆角和阴影在 Win10/11 上都干净。
/// </summary>
internal sealed class HintForm : Form
{
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WS_EX_TOPMOST = 0x00000008;
    const int WS_EX_TRANSPARENT = 0x00000020;

    const int W = 600;
    const int H = 64;
    const int MARGIN = 20;   // 距屏幕边缘
    const int FADE_MS = 140;
    const int HOLD_MS = 1150;

    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    readonly Stopwatch _clock = Stopwatch.StartNew();

    Bitmap? _bmp;
    long _startMs;
    int _holdMs = HOLD_MS;

    /// <summary>测试模式下把状态写到日志，方便脚本确认“真的显示了”。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ErrorToLog { get; set; }

    public HintForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Size = new Size(W, H);
        _timer.Tick += (_, _) => Tick();
        Show(); // 拿句柄（不激活、不可见，真正显示靠 UpdateLayeredWindow + ShowWindow）
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    public void ShowEvent(CopyEvent e, int holdMs = HOLD_MS)
    {
        _bmp?.Dispose();
        _bmp = CardPainter.Render(e, W, H);

        // 右上角
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int x = wa.Right - W - MARGIN;
        int y = wa.Top + MARGIN;
        Location = new Point(x, y);

        Render(1f);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, W, H,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        _holdMs = holdMs;
        _startMs = _clock.ElapsedMilliseconds;
        _timer.Start();

        if (ErrorToLog) Program.Log($"hint shown at {x},{y} title='{e.Title}'");
    }

    void Tick()
    {
        long t = _clock.ElapsedMilliseconds - _startMs;
        float alpha;
        if (t < FADE_MS) alpha = t / (float)FADE_MS;
        else if (t < FADE_MS + _holdMs) alpha = 1f;
        else alpha = 1f - (t - FADE_MS - _holdMs) / (float)FADE_MS;

        if (alpha <= 0f)
        {
            _timer.Stop();
            Hide();
            if (ErrorToLog) Program.Log("hint hidden");
            return;
        }
        Render(Math.Clamp(alpha, 0f, 1f));
    }

    void Render(float alpha)
    {
        if (_bmp == null) return;
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Gdi32.CreateCompatibleDC(screenDc);
        IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = _bmp.GetHbitmap(Color.FromArgb(0));
            old = Gdi32.SelectObject(memDc, hBmp);

            var size = new Gdi32.SIZE { cx = W, cy = H };
            var src = new Gdi32.POINT { x = 0, y = 0 };
            byte a = (byte)(alpha * 255);
            Gdi32.BLENDFUNCTION blend = new()
            {
                BlendOp = 0 /* AC_SRC_OVER */,
                BlendFlags = 0,
                SourceConstantAlpha = a,
                AlphaFormat = 1 /* AC_SRC_ALPHA */,
            };

            var ptDst = new Gdi32.POINT { x = Location.X, y = Location.Y };
            Native.UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref size, memDc, ref src,
                0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            if (hBmp != IntPtr.Zero)
            {
                Gdi32.SelectObject(memDc, old);
                Gdi32.DeleteObject(hBmp);
            }
            Gdi32.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    void Finish()
    {
        Hide();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _bmp?.Dispose();
            _bmp = null;
        }
        base.Dispose(disposing);
    }
}

internal static class Gdi32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr ho);
}
