using AVAQix.Models;

namespace AVAQix.Engine;

/// <summary>
/// The computer player for attract mode and the tests. On the border it weighs up boxes it could cut
/// off from where it stands: out into the open, across, and back to the border. It takes the best one
/// that keeps well clear of the qix (slowly, for double, when the qix is far off). Otherwise it runs
/// along the border, turning back from sparx coming its way; with sparx both sides it ducks out into
/// the open, where an ordinary sparx can't follow.
/// </summary>
public sealed class AttractPilot(int seed)
{
    private readonly Random _rng = new(seed);
    private List<Pt> _plan = [];
    private bool _slow;
    private bool _openOnLeft = true;
    private Dir _heading = Dir.None;
    private Pt _lastAt = new(-1, -1);

    private static readonly int[] Depths = [4, 8, 12, 18, 26];
    private static readonly int[] Widths = [6, 12, 20, 30, 44];

    public Controls Decide(GameEngine g)
    {
        var m = g.Marker;
        if (g.Phase != Phase.Playing) { _plan.Clear(); _lastAt = new(-1, -1); return default; }
        if (m.At != _lastAt) { _heading = m.Heading; _lastAt = m.At; }
        if (m.Drawing) return Draw(g);
        _plan.Clear();

        bool ahead = SparxAlong(g, _openOnLeft, 14);
        bool behind = SparxAlong(g, !_openOnLeft, 8);
        bool cornered = ahead && behind;
        if (BestBite(g, cornered) is { } bite)
        {
            _plan = bite;
            return Draw(g);
        }
        if (ahead)
        {
            _openOnLeft = !_openOnLeft;
            _heading = _heading.Opposite();
        }
        var d = g.Field.Follow(m.At, _heading, _openOnLeft);
        return new Controls(d.Dx(), d.Dy());
    }

    /// <summary>A sparx on the border within so many corners along it, going round one way.</summary>
    private static bool SparxAlong(GameEngine g, bool openOnLeft, int reach)
    {
        var p = g.Marker.At;
        var heading = Dir.None;
        var spots = new HashSet<Pt> { p };
        for (int i = 0; i < reach; i++)
        {
            heading = g.Field.Follow(p, heading, openOnLeft);
            if (heading == Dir.None) break;
            p = p.Step(heading);
            spots.Add(p);
        }
        return g.Sparx.Any(s => s.OnStix < 0 && s.Arrive < 0.4 && (spots.Contains(s.At) || s.At.Manhattan(g.Marker.At) <= 3));
    }

    private Controls Draw(GameEngine g)
    {
        var m = g.Marker;
        int i = _plan.IndexOf(m.At);
        Dir d = Dir.None;
        if (i >= 0 && i + 1 < _plan.Count)
        {
            var next = _plan[i + 1];
            d = Dirs.All.First(x => m.At.Step(x) == next);
        }
        // Off the plan somehow: head on, or turn, whichever way is open.
        if (d == Dir.None || !CanDraw(g, d))
            d = new[] { m.Heading, m.Heading.TurnLeft(), m.Heading.TurnRight() }.FirstOrDefault(x => x != Dir.None && CanDraw(g, x));
        return new Controls(d.Dx(), d.Dy(), Fast: !_slow, Slow: _slow);
    }

    private static bool CanDraw(GameEngine g, Dir d)
    {
        var m = g.Marker;
        return g.Field.IsOpen(m.At, d) && !m.Stix.Contains(m.At.Step(d)) && (!m.Drawing || d != m.Heading.Opposite());
    }

    /// <summary>The best box to cut off from here, as the corners to draw through; null if none is worth it.</summary>
    private List<Pt>? BestBite(GameEngine g, bool cornered)
    {
        var f = g.Field;
        var at = g.Marker.At;
        bool super = g.Sparx.Any(s => s.Super);
        List<Pt>? best = null;
        double bestScore = double.MinValue;
        bool bestFar = false;
        foreach (var d1 in Dirs.All)
        {
            if (!f.IsOpen(at, d1)) continue;
            foreach (var d2 in new[] { d1.TurnLeft(), d1.TurnRight() })
                foreach (int depth in Depths)
                    foreach (int width in Widths)
                    {
                        var path = Walk(f, at, (d1, depth), (d2, width), (d1.Opposite(), 400));
                        if (path is null) continue;
                        if (super && path.Count > 34) continue;
                        // Keep clear of the qix, allowing for how far it can get while the box is drawn.
                        double seconds = path.Count / GameEngine.MarkerSpeed;
                        double margin = double.MaxValue;
                        foreach (var q in g.Qixes)
                        {
                            double need = q.Length / 2 + 5 + g.QixSpeed * Math.Min(1.2, seconds) * 0.55;
                            foreach (var p in path) margin = Math.Min(margin, (p.Vec - q.Centre).Length - need);
                        }
                        if (margin < 0) continue;
                        double area = Area(path);
                        if (area < (cornered ? 0 : 40)) continue;
                        double score = area - path.Count * 3 + _rng.NextDouble() * 4;
                        if (score <= bestScore) continue;
                        bestScore = score;
                        best = path;
                        bestFar = margin > 25;
                    }
        }
        _slow = best is not null && bestFar && !super && _rng.NextDouble() < 0.35;
        return best;
    }

    /// <summary>The corners along a path of legs, ending where it meets the border; null if it can't be drawn.</summary>
    private static List<Pt>? Walk(Playfield f, Pt from, params (Dir D, int N)[] legs)
    {
        var pts = new List<Pt> { from };
        var seen = new HashSet<Pt> { from };
        var p = from;
        foreach (var (d, n) in legs)
            for (int i = 0; i < n; i++)
            {
                if (!f.IsOpen(p, d)) return null;
                p = p.Step(d);
                if (!seen.Add(p)) return null;
                pts.Add(p);
                if (f.OnBorder(p)) return pts;
            }
        return null;
    }

    /// <summary>Roughly what a path cuts off: the area between it and a straight line back to its start.</summary>
    private static double Area(List<Pt> path)
    {
        double sum = 0;
        for (int i = 0; i < path.Count; i++)
        {
            var a = path[i];
            var b = path[(i + 1) % path.Count];
            sum += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return Math.Abs(sum) / 2;
    }
}
