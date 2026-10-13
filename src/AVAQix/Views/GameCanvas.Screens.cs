using Avalonia;
using Avalonia.Media;
using AVAQix.Engine;
using AVAQix.Models;

namespace AVAQix.Views;

public sealed partial class GameCanvas
{
    /// <summary>The strip along the top: score, lives, level, how much is claimed, the sparx clock and the best score.</summary>
    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        bool playing = g.State is GameState.Playing or GameState.Paused or GameState.GameOver or GameState.EnterInitials or GameState.Attract;

        void Column(double x, string label, string value, IBrush brush)
        {
            Text(dc, label, new Point(x, 22), 28, Orange, center: true);
            Text(dc, value, new Point(x, 56), 38, brush, center: true);
        }

        Column(100, "PLAYER 1", (playing ? g.Score : 0).ToString(), White);
        Column(912, "HI-SCORE", Math.Max(g.HighScores.TopScore, g.Score).ToString(), White);
        if (!playing)
        {
            Text(dc, "AVA QIX", new Point(ScreenWidth / 2, 46), 56, Orange, center: true);
            return;
        }

        Text(dc, "LIVES", new Point(262, 22), 28, Orange, center: true);
        int spare = g.Lives - (g.Phase == Phase.Dying ? 0 : 1);
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawLife(dc, new Point(262 - (Math.Min(spare, 6) - 1) * 11 + i * 22, 56));

        Column(400, "LEVEL", g.Multiplier > 1 ? $"{g.Level}  x{g.Multiplier}" : g.Level.ToString(), White);

        double pct = g.Field.Percent;
        Text(dc, "CLAIMED", new Point(560, 22), 28, Orange, center: true);
        Text(dc, $"{pct:0.0}%", new Point(545, 56), 38, pct >= GameEngine.Threshold ? Yellow : Cyan, right: true);
        Text(dc, $"/ {GameEngine.Threshold}%", new Point(553, 58), 26, Dim);

        // The sparx clock: when it runs out two more sparx come, and then they all turn super.
        Text(dc, g.SparxStage >= 2 ? "SUPER SPARX!" : "SPARX", new Point(742, 22), 28, g.SparxStage >= 2 ? Red : Orange, center: true);
        var bar = new Rect(672, 48, 140, 14);
        dc.DrawRectangle(null, Pen(0xFF5A5A7A, 1.5), bar);
        double left = g.SparxStage >= 2 ? 1 : Math.Clamp(g.SparxTimer / g.SparxTimerFull, 0, 1);
        uint colour = g.SparxStage switch { 0 => 0xFFFFE14A, 1 => 0xFFFF8A1E, _ => (int)(_clock * 4) % 2 == 0 ? 0xFFFF3D3Du : 0xFF9A1E1Eu };
        dc.FillRectangle(Brush(colour), new Rect(bar.X + 2, bar.Y + 2, (bar.Width - 4) * left, bar.Height - 4));
        Text(dc, $"{g.Sparx.Count} ON THE LOOSE", new Point(742, 76), 20, Dim, center: true);
    }

    private static void DrawLife(DrawingContext dc, Point c)
    {
        const double r = 8;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(c + new Point(0, -r), true);
            ctx.LineTo(c + new Point(r, 0));
            ctx.LineTo(c + new Point(0, r));
            ctx.LineTo(c + new Point(-r, 0));
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(Brush(0xFFE83A3A), Pen(0xFFFFFFFF, 1.5), geo);
    }

    private static void Panel(DrawingContext dc, double top, double bottom) =>
        dc.FillRectangle(Brush(0xE0000010), new Rect(P(8, top), P(Playfield.Width - 8, bottom)));

    private static void TitlePanel(DrawingContext dc) =>
        dc.FillRectangle(Brush(0x9A000010), new Rect(P(8, 6), P(Playfield.Width - 8, 100)));

    /// <summary>The title: a qix tumbling about behind the name, and what's what.</summary>
    private void DrawTitle(DrawingContext dc)
    {
        double cx = Playfield.Width / 2.0;
        // Some claimed ground in the corners, for the look of it.
        dc.FillRectangle(Brush(FastFill[0]), new Rect(P(0, 0), P(22, 30)));
        dc.FillRectangle(Brush(SlowFill[0]), new Rect(P(138, 78), P(160, 108)));
        var pen = Pen(0xFFE8E8F4, 2.5);
        dc.DrawLine(pen, P(0, 30), P(22, 30));
        dc.DrawLine(pen, P(22, 0), P(22, 30));
        dc.DrawLine(pen, P(138, 78), P(160, 78));
        dc.DrawLine(pen, P(138, 78), P(138, 108));
        dc.DrawRectangle(null, Pen(0xFFE8E8F4, 2.5), FieldRect);

        TitlePanel(dc);
        // A qix either side of the name: bundles of lines wheeling round.
        foreach (var (x, phase) in new[] { (36.0, 0.0), (124.0, 2.1) })
            for (int i = 9; i >= 0; i--)
            {
                double t = _clock + phase - i * 0.05;
                var centre = new Vec(x + Math.Sin(t * 0.9) * 5, 17 + Math.Sin(t * 1.3) * 3);
                double a = t * 2.3, len = 7 + 2 * Math.Sin(t * 1.7);
                var d = new Vec(Math.Cos(a), Math.Sin(a)) * len;
                dc.DrawLine(Pen(Hue(t * 0.3, 1 - i * 0.07), i == 0 ? 3 : 2), P(centre + d), P(centre - d));
            }
        Text(dc, "AVA QIX", P(cx, 16), 120, Orange, center: true);
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", P(cx, 28), 30, Dim, center: true);
        Text(dc, "DRAW LINES ACROSS THE OPEN SPACE AND CLOSE THEM ON THE BORDER", P(cx, 36), 30, White, center: true);
        Text(dc, $"TO CLAIM GROUND.  CLAIM {GameEngine.Threshold}% OF THE FIELD TO CLEAR A LEVEL.", P(cx, 41), 30, White, center: true);

        double y = 51;
        void Row(Action<Point> icon, string text)
        {
            icon(P(30, y));
            Text(dc, text, P(40, y), 28, White);
            y += 7;
        }
        Row(c =>
        {
            for (int i = 0; i < 4; i++)
                dc.DrawLine(Pen(Hue(_clock * 0.3 + i * 0.08, 1 - i * 0.15), 2), c + new Point(-16, -8 + i * 4), c + new Point(16, 4 + i * 2));
        }, "THE QIX: IF IT TOUCHES A LINE YOU'RE DRAWING, YOU'RE DONE");
        Row(c =>
        {
            for (int i = 0; i < 4; i++)
            {
                double a = _clock * 10 + i * Math.PI / 2;
                dc.DrawLine(Pen(0xFFFFE14A, 2.5), c, c + new Point(Math.Cos(a) * 9, Math.Sin(a) * 9));
            }
        }, "SPARX: RUN ROUND THE BORDER AFTER YOU.  LATER THEY TURN SUPER");
        Row(c => dc.DrawEllipse(Brush((int)(_clock * 24) % 2 == 0 ? 0xFFFFE14Au : 0xFFFF6A1Au), null, c, 6, 6),
            "STOP WHILE DRAWING AND A FUSE BURNS UP YOUR LINE");
        Row(c => dc.FillRectangle(Brush(FastFill[1]), new Rect(c.X - 14, c.Y - 10, 28, 20)), "FAST DRAW (SPACE OR Z): HALF A POINT A SQUARE");
        Row(c => dc.FillRectangle(Brush(SlowFill[1]), new Rect(c.X - 14, c.Y - 10, 28, 20)), "SLOW DRAW (X OR SHIFT): A POINT A SQUARE");
        Row(c =>
        {
            dc.DrawLine(Pen(0xFFFFFFFF, 3), c + new Point(0, -12), c + new Point(0, 12));
            dc.DrawLine(Pen(Hue(0.0, 1), 2), c + new Point(-16, -6), c + new Point(-6, 6));
            dc.DrawLine(Pen(Hue(0.5, 1), 2), c + new Point(6, -6), c + new Point(16, 6));
        }, "TWO QIXES FROM LEVEL 2: SPLIT THEM TO DOUBLE YOUR SCORING");
        if (_clock % 1 < 0.65) Text(dc, "PRESS ENTER OR SPACE TO START", P(cx, 94), 34, Yellow, center: true);
        Text(dc, "© 2026 LONNIE WATSON", P(cx, 104), 24, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        double cx = Playfield.Width / 2.0;
        DrawField(dc, Engine.Field);
        Panel(dc, 6, 102);
        Text(dc, "TOP CLAIMERS", P(cx, 16), 80, Orange, center: true);
        Text(dc, "RANK      SCORE   NAME   LEVEL", P(cx, 27), 30, Dim, center: true);
        var table = Engine.HighScores.Entries;
        for (int i = 0; i < table.Count; i++)
        {
            var e = table[i];
            bool fresh = i == Engine.NewEntryIndex;
            if (fresh && _clock % 0.5 > 0.35) continue;
            var brush = fresh ? Yellow : i == 0 ? Orange : White;
            Text(dc, $"{i + 1,2}.  {e.Score,9}   {e.Initials}   {e.Stage,4}", P(cx, 35 + i * 6), 36, brush, center: true);
        }
        if (_clock % 1 < 0.65) Text(dc, "PRESS ENTER OR SPACE TO PLAY", P(cx, 97), 32, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var entry = Engine.Initials;
        if (entry is null) return;
        double cx = Playfield.Width / 2.0;
        dc.FillRectangle(Brush(0xE8000000), new Rect(P(30, 26), P(130, 84)));
        Text(dc, "A NEW HIGH SCORE!", P(cx, 34), 56, Yellow, center: true);
        Text(dc, $"{Engine.Score}", P(cx, 44), 48, White, center: true);
        for (int i = 0; i < HighScoreTable.InitialsLength; i++)
        {
            var at = P(cx - 10 + i * 10, 59);
            bool cursor = i == entry.Cursor;
            Text(dc, entry.Initials[i].ToString(), at, 90, cursor ? Yellow : White, center: true);
            if (cursor && _clock % 0.6 < 0.4)
                dc.FillRectangle(Yellow, new Rect(at.X - 20, at.Y + 40, 40, 6));
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE.  ENTER WHEN DONE", P(cx, 78), 26, Dim, center: true);
    }
}
