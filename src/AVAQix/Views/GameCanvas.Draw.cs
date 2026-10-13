using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AVAQix.Engine;
using AVAQix.Models;

namespace AVAQix.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), IPen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAQix/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
        {
            b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
            BrushCache[argb] = b;
        }
        return b;
    }

    private static IPen Pen(uint argb, double thickness)
    {
        if (!PenCache.TryGetValue((argb, thickness), out var p))
        {
            p = new ImmutablePen(argb, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            PenCache[(argb, thickness)] = p;
        }
        return p;
    }

    private static readonly IBrush White = Brush(0xFFF2F2F2);
    private static readonly IBrush Yellow = Brush(0xFFFFE14A);
    private static readonly IBrush Red = Brush(0xFFFF3D3D);
    private static readonly IBrush Orange = Brush(0xFFFF8A1E);
    private static readonly IBrush Cyan = Brush(0xFF4FD7FF);
    private static readonly IBrush Dim = Brush(0xFF8A8AA6);

    private const uint FieldBack = 0xFF05050E;
    private const uint FastStix = 0xFF6FB7FF;
    private const uint SlowStix = 0xFFFF7A3A;
    /// <summary>The claimed areas: fast in blue, slow in red, in stripes like an old screen's.</summary>
    private static readonly uint[] FastFill = [0xFF173A8A, 0xFF1E47A6];
    private static readonly uint[] SlowFill = [0xFF7E2412, 0xFF9A3118];

    /// <summary>Grid corners to the 1024×768 screen.</summary>
    private static Point P(double x, double y) => new(FieldLeft + x * Scale, FieldTop + y * Scale);
    private static Point P(Vec v) => P(v.X, v.Y);
    private static Point P(Pt p) => P(p.X, p.Y);

    private static readonly Rect FieldRect = new(P(0, 0), P(Playfield.Width, Playfield.Height));

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawHud(dc);
        dc.FillRectangle(Brush(FieldBack), FieldRect);
        using (dc.PushClip(FieldRect.Inflate(4)))
        {
            switch (g.State)
            {
                case GameState.Title:
                    DrawTitle(dc);
                    break;
                case GameState.HighScores:
                    DrawHighScores(dc);
                    break;
                default:
                    DrawPlay(dc);
                    break;
            }
        }
        Text(dc, "ARROWS MOVE   HOLD SPACE OR Z TO DRAW FAST, X OR SHIFT TO DRAW SLOW   P PAUSE   M MUTE   CTRL+S SCREENSHOT",
            new Point(ScreenWidth / 2, 756), 20, Dim, center: true);
    }

    // ----- the field ----------------------------------------------------------------------------

    private Playfield? _drawnField;
    private int _drawnVersion = -1;
    private readonly List<(Rect Rect, IBrush Brush)> _fills = [];
    private readonly List<(Point A, Point B)> _borders = [];

    /// <summary>The claimed areas and the borders, worked out again only when the field changes.</summary>
    private void Refresh(Playfield f)
    {
        if (ReferenceEquals(f, _drawnField) && f.Version == _drawnVersion) return;
        _drawnField = f;
        _drawnVersion = f.Version;
        _fills.Clear();
        _borders.Clear();
        for (int y = 0; y < Playfield.Height; y++)
        {
            int x = 0;
            while (x < Playfield.Width)
            {
                var kind = f[x, y];
                int start = x;
                while (x < Playfield.Width && f[x, y] == kind) x++;
                if (kind == Claim.Open) continue;
                var colours = kind == Claim.Fast ? FastFill : SlowFill;
                _fills.Add((new Rect(P(start, y), P(x, y + 1)), Brush(colours[(y / 2) % 2])));
            }
        }
        // Border lines: runs of lines with claimed ground one side and open the other.
        for (int y = 0; y <= Playfield.Height; y++)
        {
            int run = -1;
            for (int x = 0; x <= Playfield.Width; x++)
            {
                bool border = x < Playfield.Width && f.Closed(x, y - 1) != f.Closed(x, y);
                if (border && run < 0) run = x;
                if (!border && run >= 0) { _borders.Add((P(run, y), P(x, y))); run = -1; }
            }
        }
        for (int x = 0; x <= Playfield.Width; x++)
        {
            int run = -1;
            for (int y = 0; y <= Playfield.Height; y++)
            {
                bool border = y < Playfield.Height && f.Closed(x - 1, y) != f.Closed(x, y);
                if (border && run < 0) run = y;
                if (!border && run >= 0) { _borders.Add((P(x, run), P(x, y))); run = -1; }
            }
        }
    }

    private void DrawField(DrawingContext dc, Playfield f)
    {
        Refresh(f);
        foreach (var (rect, brush) in _fills) dc.FillRectangle(brush, rect);
        dc.DrawRectangle(null, Pen(0xFF3A3A5A, 2), FieldRect);
        var pen = Pen(0xFFE8E8F4, 2.5);
        foreach (var (a, b) in _borders) dc.DrawLine(pen, a, b);
    }

    // ----- the play -----------------------------------------------------------------------------

    private void DrawPlay(DrawingContext dc)
    {
        var g = Engine;
        DrawField(dc, g.Field);

        if (g.WasSplit && g.SplitStix.Count > 1)
            DrawPath(dc, g.SplitStix, (int)(_clock * 8) % 2 == 0 ? 0xFFFFFFFF : 0xFFFFE14A, 4);

        foreach (var q in g.Qixes) DrawQix(dc, q);

        var m = g.Marker;
        if (m.Drawing)
        {
            DrawPath(dc, m.Stix, m.Slow ? SlowStix : FastStix, 3);
            if (m.Fuse >= 0) DrawFuse(dc, m);
        }

        foreach (var s in g.Sparx) DrawSparx(dc, s);

        bool dying = g.Phase == Phase.Dying;
        if (!dying || g.PhaseTime < 0.15) DrawMarker(dc, m.At.Vec, m.Slow && m.Drawing);

        foreach (var fx in g.Effects)
        {
            switch (fx.Kind)
            {
                case EffectKind.Burst: DrawBurst(dc, fx); break;
                case EffectKind.Score:
                    Text(dc, fx.Text, P(fx.Pos.X, fx.Pos.Y - 3 - fx.Age * 4), 30, Yellow, center: true);
                    break;
            }
        }

        switch (g.State)
        {
            case GameState.Paused:
                Banner(dc, "PAUSED", "ESC TO QUIT, ANY OTHER KEY TO RESUME");
                break;
            case GameState.GameOver:
                Banner(dc, "GAME  OVER", null);
                break;
            case GameState.EnterInitials:
                DrawInitials(dc);
                break;
            default:
                if (g.Phase == Phase.Ready && g.Field.ClaimedCount == 0)
                    Banner(dc, $"LEVEL {g.Level}", g.State == GameState.Attract ? "DEMONSTRATION"
                        : g.Qixes.Count > 1 ? "TWO QIXES: SPLIT THEM FOR DOUBLE SCORING" : $"CLAIM {GameEngine.Threshold}% OF THE FIELD");
                else if (g.Phase == Phase.RoundClear)
                    Banner(dc, g.WasSplit ? "QIXES SPLIT!" : $"CLAIMED {g.LastPercent:0.0}%",
                        (g.LastBonus > 0 ? $"BONUS {g.LastBonus}" : "NO BONUS") + (g.WasSplit ? $"   NOW SCORING x{g.Multiplier}" : ""));
                else if (g.State == GameState.Attract) Text(dc, "DEMONSTRATION", P(Playfield.Width / 2.0, 6), 34, Red, center: true);
                break;
        }
    }

    private void Banner(DrawingContext dc, string title, string? sub)
    {
        double cy = Playfield.Height / 2.0;
        dc.FillRectangle(Brush(0xE0000000), new Rect(P(20, cy - 8), P(Playfield.Width - 20, cy + (sub is null ? 7 : 12))));
        Text(dc, title, P(Playfield.Width / 2.0, cy - 1), 60, Yellow, center: true);
        if (sub is not null) Text(dc, sub, P(Playfield.Width / 2.0, cy + 7), 30, White, center: true);
    }

    private static void DrawPath(DrawingContext dc, IReadOnlyList<Pt> pts, uint colour, double thickness)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(P(pts[0]), false);
            for (int i = 1; i < pts.Count; i++) ctx.LineTo(P(pts[i]));
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, Pen(colour, thickness), geo);
    }

    private void DrawFuse(DrawingContext dc, Marker m)
    {
        int whole = Math.Min((int)m.Fuse, m.Stix.Count - 1);
        var burnt = m.Stix.Take(whole + 1).ToList();
        double frac = m.Fuse - whole;
        var at = whole + 1 < m.Stix.Count ? m.Stix[whole].Vec + (m.Stix[whole + 1].Vec - m.Stix[whole].Vec) * frac : m.Stix[whole].Vec;
        if (burnt.Count > 1) DrawPath(dc, burnt, 0xFF5A2A10, 3);
        bool flick = (int)(_clock * 24) % 2 == 0;
        var c = P(at);
        dc.DrawEllipse(Brush(flick ? 0xFFFFE14Au : 0xFFFF6A1Au), null, c, 6, 6);
        for (int i = 0; i < 6; i++)
        {
            double a = i * Math.PI / 3 + _clock * 9;
            dc.DrawLine(Pen(0xFFFFB040, 2), c, c + new Point(Math.Cos(a) * 11, Math.Sin(a) * 11));
        }
    }

    /// <summary>The marker: a diamond, red and white (orange when drawing slowly).</summary>
    private void DrawMarker(DrawingContext dc, Vec at, bool slow)
    {
        var c = P(at);
        const double r = 10;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(c + new Point(0, -r), true);
            ctx.LineTo(c + new Point(r, 0));
            ctx.LineTo(c + new Point(0, r));
            ctx.LineTo(c + new Point(-r, 0));
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(Brush(slow ? 0xFFFF8A1E : 0xFFE83A3A), Pen(0xFFFFFFFF, 2), geo);
        dc.DrawEllipse(White, null, c, 3, 3);
    }

    private void DrawSparx(DrawingContext dc, Sparx s)
    {
        if (s.Arrive > 0 && (int)(s.Arrive * 12) % 2 == 0) return;
        var c = P(s.At);
        double spin = _clock * (s.Super ? 16 : 10);
        double r = s.Super ? 11 : 8;
        uint ray = s.Super ? 0xFFFF3D7Au : 0xFFFFE14Au;
        for (int i = 0; i < 4; i++)
        {
            double a = spin + i * Math.PI / 2;
            double len = r * (i % 2 == 0 ? 1 : 0.7);
            dc.DrawLine(Pen(ray, 2.5), c, c + new Point(Math.Cos(a) * len, Math.Sin(a) * len));
        }
        dc.DrawEllipse(Brush(s.Super ? 0xFFFFC0D8u : 0xFFFFFFFFu), null, c, 3.5, 3.5);
    }

    private static void DrawQix(DrawingContext dc, Qix q)
    {
        for (int i = q.Trail.Count - 1; i >= 0; i--)
        {
            var (a, b) = q.Trail[i];
            uint colour = Hue(q.Hue - i * 0.06, 1 - i * 0.07);
            dc.DrawLine(Pen(colour, 2), P(a), P(b));
        }
        dc.DrawLine(Pen(Hue(q.Hue, 1), 3), P(q.A), P(q.B));
    }

    private void DrawBurst(DrawingContext dc, Engine.Effect fx)
    {
        var c = P(fx.Pos);
        double t = fx.Age / fx.Life;
        for (int i = 0; i < 16; i++)
        {
            double a = i * Math.PI / 8 + t * 2;
            double r0 = 6 + t * 60, r1 = 14 + t * 110;
            uint colour = ((i + (int)(_clock * 20)) % 3) switch { 0 => 0xFFFFFFFFu, 1 => 0xFFFFE14Au, _ => 0xFFFF3D3Du };
            dc.DrawLine(Pen(colour, 3), c + new Point(Math.Cos(a) * r0, Math.Sin(a) * r0), c + new Point(Math.Cos(a) * r1, Math.Sin(a) * r1));
        }
    }

    /// <summary>A bright colour round the colour wheel (h from 0 to 1), dimmed by v.</summary>
    private static uint Hue(double h, double v)
    {
        h = (h % 1 + 1) % 1 * 6;
        v = Math.Clamp(v, 0.2, 1);
        double f = h - Math.Floor(h);
        (double r, double g, double b) = (int)h switch
        {
            0 => (1.0, f, 0.0),
            1 => (1 - f, 1, 0.0),
            2 => (0.0, 1, f),
            3 => (0.0, 1 - f, 1),
            4 => (f, 0.0, 1),
            _ => (1, 0.0, 1 - f),
        };
        uint B(double x) => (uint)Math.Round(Math.Clamp(0.25 + 0.75 * x, 0, 1) * v * 255);
        return 0xFF000000u | (B(r) << 16) | (B(g) << 8) | B(b);
    }

    private static void Text(DrawingContext dc, string text, Point at, double size, IBrush brush, bool center = false, bool right = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, brush);
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }
}
