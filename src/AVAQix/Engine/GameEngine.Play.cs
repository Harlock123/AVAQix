using AVAQix.Models;

namespace AVAQix.Engine;

public sealed partial class GameEngine
{
    /// <summary>The share of the field to claim, in percent, to finish a level.</summary>
    public const int Threshold = 75;
    /// <summary>Corners a second: on the border or drawing fast, and drawing slow.</summary>
    public const double MarkerSpeed = 40;
    public const double SlowSpeed = 20;
    public const double ReadySeconds = 1.4;
    public const double FirstReadySeconds = 2.4;
    public const double DyingSeconds = 2.0;
    public const double RoundClearSeconds = 4;
    /// <summary>Stand still this long while drawing and the fuse lights.</summary>
    public const double FuseDelay = 0.3;
    public const double FuseSpeed = 18;
    public const int MaxMultiplier = 8;

    public Playfield Field { get; private set; } = new();
    public Marker Marker { get; } = new();
    public List<Qix> Qixes { get; } = [];
    public List<Sparx> Sparx { get; } = [];
    /// <summary>Everything scores this many times over: doubled each time the qixes are split.</summary>
    public int Multiplier { get; private set; } = 1;
    /// <summary>0: two sparx; 1: two more have come; 2: they've all turned super.</summary>
    public int SparxStage { get; private set; }
    public double SparxTimer { get; private set; }
    public double SparxTimerFull { get; private set; } = 1;
    /// <summary>The last level ended by splitting the qixes; its stix is kept to show where.</summary>
    public bool WasSplit { get; private set; }
    public List<Pt> SplitStix { get; } = [];
    public int LastBonus { get; private set; }
    public double LastPercent { get; private set; }
    public string? LastDeath { get; private set; }
    internal List<string> LossLog { get; } = [];

    private readonly HashSet<Pt> _stixSet = [];
    private double _readyFor = ReadySeconds;
    private double _fuseSound, _trailClock;
    private int _drawTicks;
    private bool _calm, _still;

    public double SparxSpeed => Math.Min(32, 20 + 1.5 * (Level - 1));
    public double SuperSpeed => Math.Min(40, 30 + 1.5 * (Level - 1));
    public double QixSpeed => Math.Min(38, 16 + 3 * (Level - 1));
    /// <summary>How often, when it changes course, the qix goes for the marker's stix.</summary>
    public double QixHunger => Math.Min(0.5, 0.12 * (Level - 1));
    public double FirstSparxTimer => Math.Max(16, 30 - 2 * (Level - 1));
    public double SecondSparxTimer => Math.Max(12, 20 - (Level - 1));

    // ----- rounds -------------------------------------------------------------------------------

    private void StartRound()
    {
        Field = new Playfield();
        WasSplit = false;
        SplitStix.Clear();
        SetUpRound();
        SetPhase(Phase.Ready);
        _readyFor = Level == 1 ? FirstReadySeconds : ReadySeconds;
    }

    private void SetUpRound()
    {
        ClearStix();
        Marker.At = new Pt(Playfield.Width / 2, Playfield.Height);
        Marker.Heading = Dir.None;
        Marker.Move = 0;
        Qixes.Clear();
        int count = Level >= 2 ? 2 : 1;
        for (int i = 0; i < count; i++)
        {
            double x = count == 1 ? Playfield.Width / 2.0 : Playfield.Width * (i == 0 ? 0.3 : 0.7);
            var q = new Qix { Centre = new Vec(x, Playfield.Height * 0.42), Angle = i * 1.3, Hue = i * 0.5 };
            Qixes.Add(q);
            ClipQix(q);
        }
        ResetSparx();
    }

    private void ClearStix()
    {
        Marker.Stix.Clear();
        _stixSet.Clear();
        Marker.Fuse = -1;
        Marker.Stopped = 0;
        Marker.Slow = false;
    }

    private void ResetSparx()
    {
        Sparx.Clear();
        SparxStage = 0;
        SparxTimer = SparxTimerFull = FirstSparxTimer;
        if (!_calm) SpawnPair();
    }

    /// <summary>Two sparx, one each way round, from the top of the field (or the bottom, if the marker's up there).</summary>
    private void SpawnPair()
    {
        var top = Field.BorderInColumn(Playfield.Width / 2, fromTop: true);
        bool fromTop = top.Manhattan(Marker.At) > 40;
        var at = fromTop ? top : Field.BorderInColumn(Playfield.Width / 2, fromTop: false);
        foreach (bool left in new[] { true, false })
            Sparx.Add(new Sparx { At = at, OpenOnLeft = left, Heading = Field.Follow(at, Dir.None, left), Arrive = 0.8 });
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Ready:
                if (PhaseTime < _readyFor) return;
                SetPhase(Phase.Playing);
                Hum(true);
                return;
            case Phase.Dying:
                if (PhaseTime >= DyingSeconds) LoseLife();
                return;
            case Phase.RoundClear:
                if (PhaseTime < RoundClearSeconds) return;
                Level++;
                StartRound();
                return;
        }

        MoveMarker(dt, c);
        if (Phase != Phase.Playing) return;
        foreach (var q in Qixes)
        {
            if (!_still) MoveQix(q, dt);
            if (Marker.Drawing && Touches(q))
            {
                Die("the qix touched the stix");
                return;
            }
        }
        MoveSparx(dt);
        if (Phase != Phase.Playing) return;
        SparxClock(dt);
    }

    private void LoseLife()
    {
        Lives--;
        if (Lives <= 0) { GameOver(); return; }
        if (Marker.Drawing) Marker.At = Marker.Stix[0];
        ClearStix();
        Marker.Move = 0;
        ResetSparx();
        SetPhase(Phase.Ready);
        _readyFor = ReadySeconds;
    }

    private void Die(string why)
    {
        LastDeath = why;
        LossLog.Add($"L{Level} {Field.Percent:0}%: {why} at {Marker.At}");
        Effects.Add(new Effect { Kind = EffectKind.Burst, Pos = Marker.At.Vec, Life = DyingSeconds });
        Hum(false);
        Play(Sfx.Death);
        SetPhase(Phase.Dying);
    }

    /// <summary>Enough claimed, or the qixes split: the bonus for every whole percent over the mark.</summary>
    private void ClearRound()
    {
        LastPercent = Field.Percent;
        int over = Math.Max(0, (int)Math.Floor(LastPercent) - Threshold);
        LastBonus = over * 1000 * Multiplier;
        AddScore(LastBonus);
        Multiplier = WasSplit ? Math.Min(MaxMultiplier, Multiplier * 2) : 1;
        Hum(false);
        Play(WasSplit ? Sfx.Split : Sfx.RoundClear);
        SetPhase(Phase.RoundClear);
    }

    // ----- the marker ---------------------------------------------------------------------------

    private void MoveMarker(double dt, Controls c)
    {
        var m = Marker;
        if (m.Drawing && c.Fast) m.Slow = false;
        double speed = m.Drawing && m.Slow ? SlowSpeed : MarkerSpeed;
        // Out in the open it only moves with a draw button held.
        bool wants = (c.Dx != 0 || c.Dy != 0) && (!m.Drawing || c.Draw);
        bool moving = false;
        if (wants)
        {
            moving = true;
            m.Move += speed * dt;
            while (m.Move >= 1)
            {
                m.Move -= 1;
                if (!StepMarker(c)) { m.Move = 0; moving = false; break; }
                if (Phase != Phase.Playing) return;
            }
        }
        else m.Move = 0;

        if (!m.Drawing) return;
        if (moving) { m.Stopped = 0; return; }
        m.Stopped += dt;
        if (m.Stopped < FuseDelay) return;
        // Standing still out in the open: the fuse burns up the stix towards the marker.
        if (m.Fuse < 0) m.Fuse = 0;
        m.Fuse += FuseSpeed * dt;
        _fuseSound -= dt;
        if (_fuseSound <= 0) { _fuseSound = 0.16; Play(Sfx.Fuse); }
        if (m.Fuse >= m.Stix.Count - 1) Die("the fuse burnt up to the marker");
    }

    /// <summary>One corner on, if the stick allows it. Returns false if it can't go that way.</summary>
    private bool StepMarker(Controls c)
    {
        var m = Marker;
        Dir h = c.Dx < 0 ? Dir.Left : c.Dx > 0 ? Dir.Right : Dir.None;
        Dir v = c.Dy < 0 ? Dir.Up : c.Dy > 0 ? Dir.Down : Dir.None;
        // Pushed diagonally: carry on the way it's going if it can, else the other way.
        Span<Dir> tries = stackalloc Dir[2];
        int n = 0;
        if (h != Dir.None && v != Dir.None)
        {
            bool vFirst = m.Heading == v || m.Heading == v.Opposite();
            tries[n++] = vFirst ? v : h;
            tries[n++] = vFirst ? h : v;
        }
        else tries[n++] = h != Dir.None ? h : v;

        for (int i = 0; i < n; i++)
        {
            var d = tries[i];
            if (!m.Drawing)
            {
                if (Field.IsBorder(m.At, d))
                {
                    m.At = m.At.Step(d);
                    m.Heading = d;
                    return true;
                }
                if (c.Draw && Field.IsOpen(m.At, d))
                {
                    m.Stix.Add(m.At);
                    _stixSet.Add(m.At);
                    m.Slow = c.Slow && !c.Fast;
                    _drawTicks = 0;
                    Advance(d);
                    return true;
                }
            }
            else
            {
                // A stix can't double back or cross itself.
                if (d == m.Heading.Opposite() || !Field.IsOpen(m.At, d) || _stixSet.Contains(m.At.Step(d))) continue;
                Advance(d);
                return true;
            }
        }
        return false;
    }

    private void Advance(Dir d)
    {
        var m = Marker;
        m.At = m.At.Step(d);
        m.Heading = d;
        m.Stix.Add(m.At);
        _stixSet.Add(m.At);
        if (_drawTicks++ % 4 == 0) Play(m.Slow ? Sfx.DrawSlow : Sfx.DrawFast);
        if (Field.OnBorder(m.At)) CloseStix();
    }

    /// <summary>Back on the border: claim whatever the stix cut off from the qix.</summary>
    private void CloseStix()
    {
        var m = Marker;
        var stix = m.Stix.ToList();
        bool slow = m.Slow;
        var (claimed, split) = Field.Close(stix, Qixes.Select(q => q.Centre), slow ? Claim.Slow : Claim.Fast);
        ClearStix();
        int points = (int)Math.Round(claimed * (slow ? 1.0 : 0.5)) * Multiplier;
        if (points > 0)
        {
            AddScore(points);
            Popup(stix[stix.Count / 2].Vec, points.ToString());
            Play(Sfx.Claim);
        }
        foreach (var s in Sparx) Reseat(s);
        if (split)
        {
            WasSplit = true;
            SplitStix.AddRange(stix);
            ClearRound();
            return;
        }
        if (Field.Percent >= Threshold) ClearRound();
    }

    // ----- the qix ------------------------------------------------------------------------------

    private void MoveQix(Qix q, double dt)
    {
        q.Restless -= dt;
        if (q.Restless <= 0)
        {
            q.Restless = 0.25 + _rng.NextDouble() * 0.9;
            double speed = QixSpeed * (0.4 + _rng.NextDouble() * 0.8);
            double a = _rng.NextDouble() * 2 * Math.PI;
            if (Marker.Drawing && _rng.NextDouble() < QixHunger)
            {
                var to = Marker.At.Vec - q.Centre;
                a = Math.Atan2(to.Y, to.X);
            }
            q.Velocity = new Vec(Math.Cos(a), Math.Sin(a)) * speed;
            q.Spin = (_rng.NextDouble() * 2 - 1) * 6;
            q.Want = 6 + _rng.NextDouble() * 16;
        }
        // Bounce off anything claimed.
        var c = q.Centre;
        var nx = c with { X = c.X + q.Velocity.X * dt };
        if (Field.OpenAt(nx)) c = nx;
        else q.Velocity = q.Velocity with { X = -q.Velocity.X };
        var ny = c with { Y = c.Y + q.Velocity.Y * dt };
        if (Field.OpenAt(ny)) c = ny;
        else q.Velocity = q.Velocity with { Y = -q.Velocity.Y };
        q.Centre = c;
        q.Angle += q.Spin * dt;
        q.Length += Math.Clamp(q.Want - q.Length, -12 * dt, 12 * dt);
        q.Hue = (q.Hue + dt * 0.3) % 1;
        ClipQix(q);
        _trailClock += dt;
        if (_trailClock >= 0.05)
        {
            _trailClock = 0;
            foreach (var each in Qixes)
            {
                each.Trail.Insert(0, (each.A, each.B));
                if (each.Trail.Count > 9) each.Trail.RemoveAt(9);
            }
        }
    }

    /// <summary>Its line, each half cut short where it meets claimed ground.</summary>
    private void ClipQix(Qix q)
    {
        var dir = new Vec(Math.Cos(q.Angle), Math.Sin(q.Angle));
        double Reach(double sign)
        {
            double r = 0;
            for (double s = 0.5; s <= q.Length / 2; s += 0.5)
            {
                if (!Field.OpenAt(q.Centre + dir * (s * sign))) break;
                r = s;
            }
            return r;
        }
        q.A = q.Centre + dir * Reach(1);
        q.B = q.Centre + dir * -Reach(-1);
    }

    /// <summary>The qix's line (now, or a moment ago) crosses the stix being drawn.</summary>
    private bool Touches(Qix q)
    {
        var stix = Marker.Stix;
        var lines = q.Trail.Count > 0 ? new[] { (q.A, q.B), q.Trail[0] } : new[] { (q.A, q.B) };
        // The stix as straight runs from corner to corner.
        int start = 0;
        for (int i = 1; i < stix.Count; i++)
        {
            bool last = i == stix.Count - 1;
            bool turns = !last && (stix[i + 1].X - stix[i].X != stix[i].X - stix[i - 1].X || stix[i + 1].Y - stix[i].Y != stix[i].Y - stix[i - 1].Y);
            if (!turns && !last) continue;
            Vec a = stix[start].Vec, b = stix[i].Vec;
            foreach (var (p, r) in lines)
                if (Cross(p, r, a, b)) return true;
            start = i;
        }
        return false;
    }

    private static double Side(Vec a, Vec b, Vec p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    /// <summary>Two segments meet (touching counts).</summary>
    internal static bool Cross(Vec p1, Vec p2, Vec q1, Vec q2)
    {
        double d1 = Side(q1, q2, p1), d2 = Side(q1, q2, p2), d3 = Side(p1, p2, q1), d4 = Side(p1, p2, q2);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        bool On(Vec a, Vec b, Vec p, double d) =>
            Math.Abs(d) < 1e-9 && p.X >= Math.Min(a.X, b.X) - 1e-9 && p.X <= Math.Max(a.X, b.X) + 1e-9
            && p.Y >= Math.Min(a.Y, b.Y) - 1e-9 && p.Y <= Math.Max(a.Y, b.Y) + 1e-9;
        return On(q1, q2, p1, d1) || On(q1, q2, p2, d2) || On(p1, p2, q1, d3) || On(p1, p2, q2, d4);
    }

    // ----- the sparx ----------------------------------------------------------------------------

    private void MoveSparx(double dt)
    {
        var m = Marker;
        foreach (var s in Sparx)
        {
            if (s.Arrive > 0) { s.Arrive -= dt; continue; }
            s.Move += (s.Super ? SuperSpeed : SparxSpeed) * dt;
            while (s.Move >= 1)
            {
                s.Move -= 1;
                StepSparx(s);
            }
            bool caught = s.OnStix >= 0
                ? m.Drawing && s.OnStix >= m.Stix.Count - 1
                : !m.Drawing && s.At.Manhattan(m.At) <= 1;
            if (caught)
            {
                Die(s.Super ? "caught by a super sparx" : "caught by a sparx");
                return;
            }
        }
    }

    private void StepSparx(Sparx s)
    {
        var m = Marker;
        if (s.OnStix >= 0)
        {
            if (!m.Drawing) { s.OnStix = -1; Reseat(s); return; }
            s.OnStix = Math.Min(s.OnStix + 1, m.Stix.Count - 1);
            s.At = m.Stix[s.OnStix];
            return;
        }
        // A super sparx that comes to where the stix leaves the border goes up it after the marker.
        if (s.Super && m.Drawing && s.At == m.Stix[0])
        {
            s.OnStix = 0;
            return;
        }
        var d = Field.Follow(s.At, s.Heading, s.OpenOnLeft);
        if (d == Dir.None) { Reseat(s); return; }
        s.At = s.At.Step(d);
        s.Heading = d;
    }

    /// <summary>Puts a sparx back on the border after the field has changed under it.</summary>
    private void Reseat(Sparx s)
    {
        s.OnStix = -1;
        if (!Field.OnBorder(s.At)) s.At = Field.NearestBorder(s.At);
        if (Field.Follow(s.At, s.Heading, s.OpenOnLeft) == Dir.None) s.Heading = Dir.None;
    }

    private void SparxClock(double dt)
    {
        if (_calm || SparxStage >= 2) return;
        SparxTimer -= dt;
        if (SparxTimer > 0) return;
        if (SparxStage == 0)
        {
            SparxStage = 1;
            SparxTimer = SparxTimerFull = SecondSparxTimer;
            SpawnPair();
            Play(Sfx.SparxRelease);
        }
        else
        {
            SparxStage = 2;
            SparxTimer = 0;
            foreach (var s in Sparx) s.Super = true;
            Play(Sfx.SuperSparx);
        }
    }

    // ----- for the tests and screenshots --------------------------------------------------------

    internal void Idle(int steps)
    {
        for (int i = 0; i < steps; i++) Tick(default);
    }

    internal void Go()
    {
        if (Phase != Phase.Ready) return;
        SetPhase(Phase.Playing);
        Hum(true);
    }

    internal void JumpTo(int level)
    {
        Level = level;
        StartRound();
        Go();
    }

    /// <summary>No sparx, and none to come.</summary>
    internal void Calm()
    {
        _calm = true;
        Sparx.Clear();
    }

    internal void Uncalm()
    {
        _calm = false;
        ResetSparx();
    }

    /// <summary>The qixes stay where they are.</summary>
    internal void Still() => _still = true;

    internal void PutMarker(Pt at)
    {
        ClearStix();
        Marker.At = at;
        Marker.Heading = Dir.None;
    }

    internal void PutQix(int index, Vec at, double angle = 0, double length = 10)
    {
        var q = Qixes[index];
        q.Centre = at;
        q.Angle = angle;
        q.Length = q.Want = length;
        q.Trail.Clear();
        ClipQix(q);
    }

    internal Sparx PutSparx(Pt at, bool openOnLeft, bool super = false)
    {
        var s = new Sparx { At = at, OpenOnLeft = openOnLeft, Heading = Field.Follow(at, Dir.None, openOnLeft), Super = super };
        Sparx.Add(s);
        return s;
    }

    internal void ClaimBox(int x0, int y0, int x1, int y1, Claim kind = Claim.Fast) => Field.ClaimBox(x0, y0, x1, y1, kind);
}
