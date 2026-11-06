using System.Drawing.Drawing2D;

namespace FLEsync.App;

/// <summary>
/// Normec Group-huisstijl: donker marineblauw als basis, helder blauw/cyaan als accent,
/// rustige lichtgrijze achtergrond en Segoe UI. Alle kleuren staan hier centraal zodat ze
/// eenvoudig op de officiële brandguide kunnen worden afgestemd.
/// </summary>
internal static class Theme
{
    public static readonly Color Navy = ColorTranslator.FromHtml("#0A2240");      // header / primaire tekst
    public static readonly Color NavyLight = ColorTranslator.FromHtml("#163A63");
    public static readonly Color Blue = ColorTranslator.FromHtml("#0062AE");      // primaire knoppen
    public static readonly Color BlueHover = ColorTranslator.FromHtml("#00508F");
    public static readonly Color Accent = ColorTranslator.FromHtml("#00A9CE");    // accentlijnen / highlights
    public static readonly Color Background = ColorTranslator.FromHtml("#F3F5F8");
    public static readonly Color Card = Color.White;
    public static readonly Color Border = ColorTranslator.FromHtml("#DDE3EA");
    public static readonly Color Text = ColorTranslator.FromHtml("#1B2A3A");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#5E6E80");
    public static readonly Color Success = ColorTranslator.FromHtml("#2E9E5B");
    public static readonly Color Warning = ColorTranslator.FromHtml("#E39B1B");
    public static readonly Color Danger = ColorTranslator.FromHtml("#C8372D");

    public static readonly Font Base = new("Segoe UI", 9.75f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.75f);
    public static readonly Font CardTitle = new("Segoe UI Semibold", 11f);
    public static readonly Font Logo = new("Segoe UI", 20f, FontStyle.Bold);
    public static readonly Font Subtitle = new("Segoe UI", 9f);
    public static readonly Font Mono = new("Consolas", 9f);

    public static Button PrimaryButton(string text) => MakeButton(text, Blue, Color.White, BlueHover);
    public static Button AccentButton(string text) => MakeButton(text, Accent, Color.White, ColorTranslator.FromHtml("#0090B0"));
    public static Button SecondaryButton(string text) => MakeButton(text, Color.White, Navy, ColorTranslator.FromHtml("#E8EEF5"), Border);
    public static Button DangerButton(string text) => MakeButton(text, Color.White, Danger, ColorTranslator.FromHtml("#FBECEB"), Danger);

    private static Button MakeButton(string text, Color back, Color fore, Color hover, Color? border = null)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = Bold,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 6, 14, 6),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = border.HasValue ? 1 : 0;
        b.FlatAppearance.BorderColor = border ?? back;
        b.FlatAppearance.MouseOverBackColor = hover;
        b.FlatAppearance.MouseDownBackColor = hover;
        return b;
    }

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Icon? AppIcon()
    {
        using var stream = typeof(Theme).Assembly.GetManifestResourceStream("flesync.ico");
        return stream is null ? null : new Icon(stream);
    }
}

/// <summary>Witte kaart met titel en accentstreep, in Normec-stijl.</summary>
internal sealed class Card : Panel
{
    private readonly string _title;

    public Card(string title)
    {
        _title = title;
        BackColor = Theme.Background;
        Padding = new Padding(20, 46, 20, 16);
        Margin = new Padding(0, 0, 0, 14);
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(rect, 6))
        {
            using var fill = new SolidBrush(Theme.Card);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        using (var accent = new SolidBrush(Theme.Accent))
            g.FillRectangle(accent, 20, 16, 4, 20);
        TextRenderer.DrawText(g, _title, Theme.CardTitle, new Point(30, 15), Theme.Navy);
    }
}

/// <summary>Gekleurd statuslabel ("pil") voor de servicestatus.</summary>
internal sealed class StatusPill : Control
{
    private Color _dot = Theme.TextMuted;

    public StatusPill()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Font = Theme.Bold;
        Height = 30;
        Width = 230;
    }

    public void SetStatus(string text, Color dot)
    {
        Text = text;
        _dot = dot;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(rect, Height / 2 - 1))
        using (var fill = new SolidBrush(Theme.NavyLight))
            g.FillPath(fill, path);
        using (var dot = new SolidBrush(_dot))
            g.FillEllipse(dot, 12, Height / 2 - 5, 10, 10);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 0, Width - 34, Height), Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
