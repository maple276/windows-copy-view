using System.Drawing.Drawing2D;

namespace CopyHint;

/// <summary>一次复制事件的描述（文本复制 / 文件复制）。</summary>
internal sealed class CopyEvent
{
    public bool IsFile;
    public bool Warning;
    /// <summary>重要提示（比如复制失败）停留更久。</summary>
    public bool Sticky;
    public string Title = "";
    public string Detail = "";

    /// <summary>同一内容再次复制时用来抑制重复气泡。</summary>
    public string Fingerprint = "";

    public static CopyEvent ForText(string text)
    {
        if (PathHeuristics.LooksLikePath(text))
        {
            string p = text.Trim().Trim('"');
            string name = SafeName(p);
            string dir = SafeDir(p);
            return new CopyEvent
            {
                IsFile = true,
                Title = name,
                Detail = "路径已复制  ·  " + dir,
                Fingerprint = "P:" + p,
            };
        }

        string trimmed = text.Trim();
        string flat = trimmed.Replace("\r", "").Replace("\n", " ⏎ ");
        const int max = 40;
        string preview = flat.Length <= max ? flat : flat[..max] + "…";
        if (preview.Length == 0) preview = "（空）";
        return new CopyEvent
        {
            IsFile = false,
            Title = "已复制 " + trimmed.Length + " 个字符",
            Detail = preview,
            Fingerprint = "T:" + trimmed,
        };
    }

    public static CopyEvent ForFiles(string[] paths)
    {
        if (paths.Length == 0)
        {
            return new CopyEvent { IsFile = true, Title = "已复制文件", Detail = "文件列表" };
        }

        if (paths.Length == 1)
        {
            string p = paths[0];
            return new CopyEvent
            {
                IsFile = true,
                Title = SafeName(p),
                Detail = "文件已复制  ·  " + SafeDir(p),
                Fingerprint = "F:" + p,
            };
        }

        return new CopyEvent
        {
            IsFile = true,
            Title = "已复制 " + paths.Length + " 个文件",
            Detail = "位于 " + SafeDir(paths[0]),
            Fingerprint = "F:" + string.Join("|", paths),
        };
    }

    static string SafeName(string p)
    {
        try
        {
            string n = Path.GetFileName(p.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(n) ? p : n;
        }
        catch { return p; }
    }

    static string SafeDir(string p)
    {
        try
        {
            string d = Directory.Exists(p) ? p : Path.GetDirectoryName(p) ?? "";
            return d.TrimEnd('\\');
        }
        catch { return ""; }
    }
}

/// <summary>在内存里把卡片画成一张位图。</summary>
internal static class CardPainter
{
    public static Bitmap Render(CopyEvent e, int w, int h, int radius = 14)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        using (var path = RoundRect(new Rectangle(0, 0, w - 1, h - 1), radius))
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                   Color.FromArgb(46, 52, 62), Color.FromArgb(28, 32, 39), LinearGradientMode.Vertical))
        using (var border = new Pen(Color.FromArgb(90, 255, 255, 255)))
        {
            g.FillPath(bg, path);
            g.DrawPath(border, path);
        }

        // 左侧状态指示条：文件 = 琥珀色，文本 = 薄荷绿，复制失败提醒 = 珊瑚红
        Color accent = e.Warning ? Color.FromArgb(255, 120, 105)
                     : e.IsFile ? Color.FromArgb(255, 186, 74)
                     : Color.FromArgb(86, 214, 160);
        using (var acc = new SolidBrush(accent))
        using (var accPath = RoundRect(new Rectangle(6, 8, 4, h - 16), 2))
        {
            g.FillPath(acc, accPath);
        }

        using var icon = DrawIcon(e, accent);
        int iconY = (h - icon.Height) / 2;
        g.DrawImage(icon, 18, iconY, icon.Width, icon.Height);

        int textLeft = 18 + icon.Width + 12;
        int textWidth = Math.Max(10, w - textLeft - 18);
        // 气泡宽度会在 300（紧凑）到 600（默认，约两倍长）之间变化，字号和留白按宽度折中
        bool wide = w >= 480;
        using var titleFont = new Font("Microsoft YaHei UI", wide ? 11f : 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var detailFont = new Font("Microsoft YaHei UI", wide ? 9f : 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        using var titleBrush = new SolidBrush(Color.FromArgb(244, 246, 250));
        using var detailBrush = new SolidBrush(Color.FromArgb(154, 162, 175));
        using var fmt = new StringFormat(StringFormatFlags.NoWrap)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        bool twoLines = h >= 56;
        float titleY = twoLines ? iconY + 2 : (h - titleFont.GetHeight(g)) / 2f;
        g.DrawString(e.Title, titleFont, titleBrush, new RectangleF(textLeft, titleY, textWidth, titleFont.GetHeight(g) + 4), fmt);
        if (twoLines)
        {
            float dy = titleY + titleFont.GetHeight(g) + 1;
            g.DrawString(e.Detail, detailFont, detailBrush, new RectangleF(textLeft, dy, textWidth, detailFont.GetHeight(g) + 2), fmt);
        }

        return bmp;
    }

    /// <summary>画一个 32x32 的小图标：文本 = 带行线的纸片，文件 = 带折角的文件，警告 = 感叹号圆圈。</summary>
    static Bitmap DrawIcon(CopyEvent e, Color accent)
    {
        bool isFile = e.IsFile;
        var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var fill = new SolidBrush(Color.FromArgb(38, accent));
        using var pen = new Pen(accent, 2f);
        using var line = new Pen(Color.FromArgb(230, accent), 1.6f);

        if (e.Warning)
        {
            g.DrawEllipse(pen, 5, 5, 22, 22);
            g.DrawLine(line, 16, 10, 16, 19);
            using var dot = new SolidBrush(accent);
            g.FillEllipse(dot, 14.5f, 21.5f, 3.2f, 3.2f);
            return bmp;
        }

        if (!isFile)
        {
            var doc = new RectangleF(7, 5, 18, 22);
            using (var p = RoundRect(Rectangle.Round(doc), 3)) { g.FillPath(fill, p); g.DrawPath(pen, p); }
            for (int i = 0; i < 3; i++)
            {
                float y = 11 + i * 4.5f;
                g.DrawLine(line, 11, y, i == 2 ? 17 : 21, y);
            }
        }
        else
        {
            var pts = new[]
            {
                new PointF(7, 4), new PointF(19, 4), new PointF(25, 10),
                new PointF(25, 28), new PointF(7, 28),
            };
            using var p = new GraphicsPath();
            p.AddPolygon(pts);
            p.CloseFigure();
            g.FillPath(fill, p);
            g.DrawPath(pen, p);
            g.DrawLine(line, 19, 4, 19, 10);
            g.DrawLine(line, 19, 10, 25, 10);
        }

        return bmp;
    }

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        int d = Math.Min(Math.Min(radius * 2, r.Width), r.Height);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
