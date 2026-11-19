using System.Drawing;

namespace CopyHint;

/// <summary>仅用于自动化测试：一个占住前台的窗口，收到 -test 参数后自己模拟“拖选 + Ctrl+C”。</summary>
internal static class TestWindow
{
    public static void Run(string mode)
    {
        var f = new Form
        {
            Text = "CopyHint Test Window",
            Width = 520,
            Height = 320,
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = Color.White,
            TopMost = true,
            ShowInTaskbar = true,
        };
        var label = new Label
        {
            Text = "CopyHint 测试窗口\n这里是可被选中的文字内容 ABCDEFG",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 14f),
        };
        f.Controls.Add(label);

        f.Shown += (_, _) =>
        {
            f.Activate();
            Program.Log("test window shown");
            Task.Delay(700).ContinueWith(_ => f.Invoke(() =>
            {
                var r = label.RectangleToScreen(label.ClientRectangle);
                if (mode == "nocopy") TestActions.DragSelectOnly(f.Handle, r);
                else TestActions.DragSelectAndCopy(f.Handle, r);
                Task.Delay(4000).ContinueWith(__ => f.Invoke(() => f.Close()));
            }));
        };

        Application.Run(f);
    }
}
