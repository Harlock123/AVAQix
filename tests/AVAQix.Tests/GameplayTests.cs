using AVAQix.Engine;
using AVAQix.Models;

namespace AVAQix.Tests;

public sealed class GameplayTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qix-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private const int W = Playfield.Width, H = Playfield.Height;

    /// <summary>A level under way with no sparx and the qix keeping still in the middle.</summary>
    private GameEngine Calm(int level = 1, int seed = 1)
    {
        var g = new GameEngine(seed, new HighScoreTable(_dir));
        g.StartGame();
        g.JumpTo(level);
        g.Calm();
        g.Still();
        g.PutQix(0, new Vec(W / 2.0, 40));
        return g;
    }

    private static void Steps(GameEngine g, int n, Controls c = default)
    {
        for (int i = 0; i < n; i++) g.Tick(c);
    }

    /// <summary>Holds a direction (and the draw buttons) until the marker has gone so many corners, or can't.</summary>
    private static void Go(GameEngine g, Dir d, int corners, bool fast = false, bool slow = false)
    {
        var c = new Controls(d.Dx(), d.Dy(), fast, slow);
        int moved = 0, idle = 0;
        var at = g.Marker.At;
        while (moved < corners && idle < 30 && g.Phase == Phase.Playing)
        {
            g.Tick(c);
            if (g.Marker.At != at) { moved++; idle = 0; at = g.Marker.At; }
            else idle++;
        }
    }

    [Fact]
    public void The_frame_is_the_border_and_the_inside_is_open()
    {
        var f = new Playfield();
        Assert.True(f.IsBorder(new Pt(0, 0), Dir.Right));
        Assert.True(f.IsBorder(new Pt(W, 10), Dir.Down));
        Assert.False(f.IsBorder(new Pt(10, 10), Dir.Right));
        Assert.True(f.IsOpen(new Pt(10, 10), Dir.Up));
        Assert.False(f.IsOpen(new Pt(10, 0), Dir.Right));
        Assert.False(f.IsBorder(new Pt(0, 0), Dir.Up));
        // Following the border keeping the open side on the left goes all the way round, anticlockwise.
        var p = new Pt(W / 2, 0);
        var d = f.Follow(p, Dir.None, openOnLeft: true);
        Assert.Equal(Dir.Left, d);
        int n = 0;
        do
        {
            p = p.Step(d);
            d = f.Follow(p, d, true);
            n++;
        } while (p != new Pt(W / 2, 0) && n < 10000);
        Assert.Equal(2 * (W + H), n);
    }

    [Fact]
    public void The_marker_runs_round_the_border_and_only_goes_out_with_a_draw_button()
    {
        var g = Calm();
        Assert.Equal(new Pt(W / 2, H), g.Marker.At);
        Go(g, Dir.Right, 10);
        Assert.Equal(new Pt(W / 2 + 10, H), g.Marker.At);
        Go(g, Dir.Up, 5);
        Assert.Equal(new Pt(W / 2 + 10, H), g.Marker.At);
        Assert.False(g.Marker.Drawing);
        Go(g, Dir.Up, 5, fast: true);
        Assert.True(g.Marker.Drawing);
        Assert.Equal(new Pt(W / 2 + 10, H - 5), g.Marker.At);
        // Let go of the button and it stops.
        Steps(g, 10, new Controls(0, -1));
        Assert.Equal(new Pt(W / 2 + 10, H - 5), g.Marker.At);
    }

    [Fact]
    public void Closing_a_stix_claims_the_side_away_from_the_qix()
    {
        var g = Calm();
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        Go(g, Dir.Up, 10, fast: true);
        Go(g, Dir.Right, 20, fast: true);
        Go(g, Dir.Down, 10, fast: true);
        Assert.False(g.Marker.Drawing);
        Assert.Equal(new Pt(W / 2 + 20, H), g.Marker.At);
        Assert.Equal(200, g.Field.ClaimedCount);
        Assert.Equal(Claim.Fast, g.Field[W / 2 + 5, H - 5]);
        Assert.Equal(100, g.Score);
        Assert.Contains(Sfx.Claim, heard);
        Assert.Contains(Sfx.DrawFast, heard);
        // The new edge is border now: run along the top of the claimed box.
        Go(g, Dir.Up, 10);
        Go(g, Dir.Left, 20);
        Assert.Equal(new Pt(W / 2, H - 10), g.Marker.At);
    }

    [Fact]
    public void A_slow_draw_scores_double_unless_you_speed_up()
    {
        var g = Calm();
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        Go(g, Dir.Up, 10, slow: true);
        Assert.True(g.Marker.Slow);
        Go(g, Dir.Right, 20, slow: true);
        Go(g, Dir.Down, 10, slow: true);
        Assert.Equal(200, g.Score);
        Assert.Equal(Claim.Slow, g.Field[W / 2 + 5, H - 5]);
        Assert.Contains(Sfx.DrawSlow, heard);

        // Slow is half the speed.
        g = Calm();
        g.Tick(new Controls(0, -1, Slow: true));
        Steps(g, 60, new Controls(0, -1, Slow: true));
        Assert.InRange(H - g.Marker.At.Y, 19, 22);

        g = Calm();
        Go(g, Dir.Up, 10, slow: true);
        Go(g, Dir.Right, 20, fast: true);
        Assert.False(g.Marker.Slow);
        Go(g, Dir.Down, 10, fast: true);
        Assert.Equal(100, g.Score);
    }

    [Fact]
    public void A_stix_cannot_double_back_or_cross_itself()
    {
        var g = Calm();
        Go(g, Dir.Up, 10, fast: true);
        var at = g.Marker.At;
        Go(g, Dir.Down, 3, fast: true);
        Assert.Equal(at, g.Marker.At);
        Go(g, Dir.Right, 4, fast: true);
        Go(g, Dir.Down, 4, fast: true);
        Go(g, Dir.Left, 8, fast: true);
        // Stopped short of the stix going up.
        Assert.Equal(new Pt(W / 2 + 1, H - 6), g.Marker.At);
        Assert.True(g.Marker.Drawing);
    }

    [Fact]
    public void Standing_still_out_in_the_open_lights_a_fuse()
    {
        var g = Calm();
        Go(g, Dir.Up, 20, fast: true);
        Steps(g, 30);
        Assert.True(g.Marker.Fuse > 0);
        // Moving on holds it where it is.
        double fuse = g.Marker.Fuse;
        Go(g, Dir.Right, 6, fast: true);
        Assert.Equal(fuse, g.Marker.Fuse, 3);
        // Stand still long enough and it reaches the marker.
        Steps(g, 60 * 3);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Contains("fuse", g.LastDeath);
        Steps(g, (int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(3, g.Lives);
        Assert.Equal(new Pt(W / 2, H), g.Marker.At); // back where the stix began
        Assert.False(g.Marker.Drawing);
    }

    [Fact]
    public void The_qix_touching_the_stix_is_the_end_of_it()
    {
        var g = Calm();
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.PutQix(0, new Vec(W / 2.0 + 0.5, H - 30), angle: 0, length: 12);
        Go(g, Dir.Up, 40, fast: true);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Contains("qix", g.LastDeath);
        Assert.Contains(Sfx.Death, heard);
        Assert.Contains(g.Effects, e => e.Kind == EffectKind.Burst);
        Assert.True(GameEngine.Cross(new Vec(0, 0), new Vec(2, 2), new Vec(0, 2), new Vec(2, 0)));
        Assert.False(GameEngine.Cross(new Vec(0, 0), new Vec(1, 0), new Vec(0, 1), new Vec(1, 1)));
    }

    [Fact]
    public void The_qix_bounces_about_the_open_space_and_never_strays_into_claimed_ground()
    {
        var g = new GameEngine(3, new HighScoreTable(_dir));
        g.StartGame();
        g.JumpTo(3);
        g.Calm();
        g.ClaimBox(0, 0, 79, 107);
        g.ClaimBox(80, 60, 159, 107);
        g.PutQix(0, new Vec(120, 30));
        g.PutQix(1, new Vec(100, 20));
        var start = g.Qixes[0].Centre;
        for (int i = 0; i < 60 * 20; i++)
        {
            g.Tick(default);
            foreach (var q in g.Qixes)
            {
                Assert.True(g.Field.OpenAt(q.Centre), $"centre at {q.Centre}");
                Assert.True(g.Field.OpenAt(q.A) && g.Field.OpenAt(q.B), "line in the open");
            }
        }
        Assert.NotEqual(start, g.Qixes[0].Centre);
    }

    [Fact]
    public void Sparx_run_the_border_and_catch_the_marker_there_but_not_out_drawing()
    {
        var g = Calm();
        var s = g.PutSparx(new Pt(W / 2 + 30, H), openOnLeft: false);
        Assert.Equal(Dir.Left, s.Heading);
        for (int i = 0; i < 60 * 3 && g.Phase == Phase.Playing; i++) g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("caught by a sparx", g.LastDeath);

        g = Calm();
        Go(g, Dir.Up, 6, fast: true);
        s = g.PutSparx(new Pt(W / 2 + 10, H), openOnLeft: false);
        var c = new Controls(0, 0, Fast: true);
        for (int i = 0; i < 60; i++) g.Tick(i % 10 == 0 ? new Controls(1, 0, Fast: true) : c);
        Assert.Equal(Phase.Playing, g.Phase);
        Assert.True(s.At.X < W / 2, "went on past");
    }

    [Fact]
    public void A_super_sparx_chases_the_marker_up_its_stix()
    {
        var g = Calm();
        Go(g, Dir.Up, 30, slow: true);
        g.PutSparx(new Pt(W / 2 + 6, H), openOnLeft: false, super: true);
        for (int i = 0; i < 60 * 6 && g.Phase == Phase.Playing; i++) g.Tick(new Controls(1, 0, Slow: true));
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("caught by a super sparx", g.LastDeath);
    }

    [Fact]
    public void The_sparx_clock_runs_out_twice_and_they_turn_super()
    {
        var g = Calm();
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Uncalm();
        Assert.Equal(2, g.Sparx.Count);
        Assert.Equal(new Pt(W / 2, 0), g.Sparx[0].At);
        // Hold the sparx where they are so they can't catch the marker meanwhile.
        void Wait(double seconds)
        {
            for (int i = 0; i < 60 * seconds; i++)
            {
                foreach (var s in g.Sparx) s.Arrive = 1;
                g.Tick(default);
            }
        }
        Wait(g.FirstSparxTimer + 0.5);
        Assert.Equal(4, g.Sparx.Count);
        Assert.Contains(Sfx.SparxRelease, heard);
        Assert.False(g.Sparx[0].Super);
        Wait(g.SecondSparxTimer + 0.5);
        Assert.Equal(2, g.SparxStage);
        Assert.Contains(Sfx.SuperSparx, heard);
        Assert.All(g.Sparx, s => Assert.True(s.Super));
    }

    [Fact]
    public void Sparx_on_ground_just_claimed_are_put_back_on_the_border()
    {
        var g = Calm();
        var s = g.PutSparx(new Pt(W / 2 + 5, H), openOnLeft: true);
        s.Arrive = 100; // hold it there
        Go(g, Dir.Up, 10, fast: true);
        Go(g, Dir.Right, 20, fast: true);
        Go(g, Dir.Down, 10, fast: true);
        Assert.True(g.Field.OnBorder(s.At), $"at {s.At}");
        Assert.NotEqual(new Pt(W / 2 + 5, H), s.At);
        Assert.True(s.At.Manhattan(new Pt(W / 2 + 5, H)) <= 5);
    }

    [Fact]
    public void Claiming_three_quarters_clears_the_level_with_a_bonus_for_every_percent_over()
    {
        var g = Calm();
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        // Claim the left side, then cut off a strip of what's left to pass 75%.
        g.ClaimBox(0, 0, 109, 107);
        g.PutQix(0, new Vec(145, 30));
        g.PutMarker(new Pt(140, H));
        Go(g, Dir.Up, 5, fast: true);
        Go(g, Dir.Right, 5, fast: true);
        Go(g, Dir.Down, 5, fast: true);
        Assert.Equal(Phase.Playing, g.Phase); // 69% so far
        g.PutMarker(new Pt(112, H));
        Go(g, Dir.Up, 70, fast: true);
        Assert.True(g.Marker.Drawing);
        Go(g, Dir.Right, 20, fast: true);
        Go(g, Dir.Down, 70, fast: true);
        Assert.Equal(Phase.RoundClear, g.Phase);
        double pct = g.Field.Percent;
        Assert.True(pct >= 75);
        Assert.Equal(((int)pct - 75) * 1000, g.LastBonus);
        Assert.Contains(Sfx.RoundClear, heard);
        Steps(g, (int)(60 * GameEngine.RoundClearSeconds) + 2);
        Assert.Equal(2, g.Level);
        Assert.Equal(0, g.Field.ClaimedCount);
        Assert.Equal(2, g.Qixes.Count);
    }

    [Fact]
    public void Splitting_the_two_qixes_ends_the_level_and_doubles_the_scoring()
    {
        var g = Calm(level: 2);
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.PutQix(0, new Vec(40, 50), length: 6);
        g.PutQix(1, new Vec(120, 50), length: 6);
        g.PutMarker(new Pt(W / 2, 0));
        Go(g, Dir.Down, H, fast: true);
        Assert.Equal(Phase.RoundClear, g.Phase);
        Assert.True(g.WasSplit);
        Assert.Equal(2, g.Multiplier);
        Assert.Contains(Sfx.Split, heard);
        Assert.Equal(0, g.Field.ClaimedCount);
        Steps(g, (int)(60 * GameEngine.RoundClearSeconds) + 2);
        Assert.Equal(3, g.Level);
        g.Go();
        g.Calm();
        g.Still();
        g.PutQix(0, new Vec(80, 30));
        g.PutQix(1, new Vec(90, 30));
        int before = g.Score;
        Go(g, Dir.Up, 10, fast: true);
        Go(g, Dir.Right, 20, fast: true);
        Go(g, Dir.Down, 10, fast: true);
        Assert.Equal(before + 200, g.Score); // 200 cells, half a point each, doubled
    }

    [Fact]
    public void Bonus_lives_at_20000_and_every_40000_after()
    {
        var g = Calm();
        Assert.Equal(4, g.Lives);
        g.AddScore(19999);
        Assert.Equal(4, g.Lives);
        g.AddScore(1);
        Assert.Equal(5, g.Lives);
        g.AddScore(40000);
        Assert.Equal(6, g.Lives);
    }

    [Fact]
    public void The_demonstration_pilot_clears_levels_and_the_game_ends_in_the_table()
    {
        var g = new GameEngine(1, new HighScoreTable(_dir));
        g.StartGame();
        var pilot = new AttractPilot(1);
        for (int i = 0; i < 60 * 60 * 8 && g.State == GameState.Playing && g.Level < 3; i++) g.Tick(pilot.Decide(g));
        Assert.True(g.Level >= 3, $"reached level {g.Level} at {g.Field.Percent:0}%; lost: {string.Join("; ", g.LossLog)}");
        // Then stand still out in the open until the fuses finish it.
        for (int i = 0; i < 60 * 60 * 10 && g.State == GameState.Playing; i++)
            g.Tick(g.Marker.Drawing ? default : new Controls(0, g.Marker.At.Y == 0 ? 1 : -1, Fast: true));
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(g.Score, HighScoreTable.Load(_dir).TopScore);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Run()
        {
            var g = new GameEngine(21, new HighScoreTable(_dir));
            g.StartGame();
            var pilot = new AttractPilot(5);
            for (int i = 0; i < 60 * 60; i++) g.Tick(pilot.Decide(g));
            return $"{g.Level} {g.Score} {g.Lives} {g.Marker.At} {g.Field.ClaimedCount} {g.Qixes[0].Centre} {g.Sparx.Count}";
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void The_title_plays_its_tune_and_the_demonstration_has_sound()
    {
        var g = new GameEngine(5, new HighScoreTable(_dir));
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Idle(1);
        Assert.Equal([Sfx.TitleTune], heard);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + 10)));
        Assert.Equal(GameState.Attract, g.State);
        Assert.Contains(Sfx.StartTune, heard);
        Assert.Contains(Sfx.Hum, heard);
        Assert.Contains(Sfx.Claim, heard);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 8)));
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(Sfx.HumStop, heard.Last(s => s is Sfx.Hum or Sfx.HumStop));
        g.Idle((int)(60 * (GameEngine.HighScoreSeconds + 1)));
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void Pausing_holds_everything()
    {
        var g = Calm();
        g.Pause();
        Steps(g, 30, new Controls(1, 0));
        Assert.Equal(new Pt(W / 2, H), g.Marker.At);
        g.Resume();
        Go(g, Dir.Right, 3);
        Assert.Equal(new Pt(W / 2 + 3, H), g.Marker.At);
    }

    [Fact]
    public void Every_sound_is_there()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        var files = Enum.GetValues<Sfx>().Where(s => s != Sfx.HumStop).Select(s => s.ToString());
        Assert.All(files, f => Assert.True(File.Exists(Path.Combine(folder, f + ".wav")), f));
    }
}
