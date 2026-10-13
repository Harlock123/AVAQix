using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAQix.Engine;
using AVAQix.Models;
using AVAQix.Views;
using Controls = AVAQix.Engine.Controls;

namespace AVAQix.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set QIX_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qix-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(_dir));
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("QIX_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAQix-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

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

    [AvaloniaFact]
    public void The_title_and_a_level_under_way()
    {
        var (w, c, g) = Open();
        g.Idle(60 * 2);
        Shot(w, c, "title");
        g.StartGame();
        g.Go();
        var pilot = new AttractPilot(3);
        for (int i = 0; i < 60 * 60 * 3 && g.State == GameState.Playing && g.Level == 1; i++)
        {
            g.Tick(pilot.Decide(g));
            if (g.Field.Percent > 48 && g.Phase == Phase.Playing && g.Marker.Stix.Count > 24) break;
        }
        Shot(w, c, "play");
        w.Close();
    }

    [AvaloniaFact]
    public void Two_qixes_a_slow_draw_and_a_fuse()
    {
        var (w, c, g) = Open(seed: 5);
        g.StartGame();
        g.JumpTo(2);
        g.Still();
        g.ClaimBox(0, 0, 29, 40, Claim.Fast);
        g.ClaimBox(0, 41, 17, 107, Claim.Slow);
        g.ClaimBox(130, 70, 159, 107, Claim.Fast);
        g.ClaimBox(100, 0, 159, 14, Claim.Slow);
        g.PutQix(0, new Vec(58, 52), angle: 0.6, length: 18);
        g.PutQix(1, new Vec(118, 44), angle: 2.2, length: 14);
        for (int i = 0; i < 40; i++) g.Tick(default);
        g.PutMarker(new Pt(84, 108));
        Go(g, Dir.Up, 30, slow: true);
        Go(g, Dir.Right, 26, slow: true);
        Go(g, Dir.Down, 8, slow: true);
        g.Idle(48);
        Assert.True(g.Marker.Fuse > 0);
        Shot(w, c, "fuse");
        w.Close();
    }

    [AvaloniaFact]
    public void Splitting_the_qixes()
    {
        var (w, c, g) = Open(seed: 6);
        g.StartGame();
        g.JumpTo(2);
        g.Calm();
        g.Still();
        g.ClaimBox(0, 0, 159, 12, Claim.Fast);
        g.ClaimBox(0, 90, 50, 107, Claim.Slow);
        g.PutQix(0, new Vec(40, 50), angle: 0.4, length: 16);
        g.PutQix(1, new Vec(120, 60), angle: 1.9, length: 16);
        for (int i = 0; i < 20; i++) g.Tick(default);
        g.PutMarker(new Pt(80, 13));
        Go(g, Dir.Down, 40, fast: true);
        Go(g, Dir.Right, 6, fast: true);
        Go(g, Dir.Down, 100, fast: true);
        Assert.True(g.WasSplit);
        g.Idle(30);
        Shot(w, c, "split");
        w.Close();
    }

    [AvaloniaFact]
    public void Caught_and_the_high_scores()
    {
        var (w, c, g) = Open(seed: 4);
        g.StartGame();
        g.Go();
        g.Still();
        g.PutQix(0, new Vec(70, 70), angle: 0.3, length: 22);
        Go(g, Dir.Up, 60, fast: true);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Idle(14);
        Shot(w, c, "caught");

        // Out of lives: stand still out in the open until the fuse finishes each one.
        for (int i = 0; i < 60 * 60 && g.State == GameState.Playing; i++)
            g.Tick(g.Marker.Drawing ? default : new Controls(0, -1, Fast: true));
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        if (g.State == GameState.EnterInitials) { g.Initials!.Finish(); g.Idle(5); }
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle(20);
        Shot(w, c, "high-scores");
        w.Close();
    }
}
