using AVAQix.Models;

namespace AVAQix.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>What is happening within a game.</summary>
public enum Phase
{
    /// <summary>A moment before the qix gets going.</summary>
    Ready,
    Playing,
    /// <summary>The marker caught: it bursts, and everything starts over from where it was.</summary>
    Dying,
    /// <summary>Enough of the field claimed (or the qixes split): the bonus is counted.</summary>
    RoundClear,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    /// <summary>The qix's drone, looping while it moves.</summary>
    Hum, HumStop,
    DrawFast, DrawSlow, Claim, Fuse, Death, SparxRelease, SuperSparx, Split, ExtraLife,
    TitleTune, StartTune, RoundClear, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it
/// runs in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always plays the
/// same game.
/// </summary>
public sealed partial class GameEngine
{
    public const double Step = 1.0 / 60;
    public const int StartingLives = 4;
    public const int FirstExtraLife = 20000;
    public const int ExtraLifeEvery = 40000;
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;

    private readonly Random _rng;
    private double _accumulator;
    private bool _startPrev;
    private bool _titleTunePending = true;
    private int _nextExtraLife;
    private bool _hum;

    public GameEngine(int seed = 1981, HighScoreTable? table = null)
    {
        _rng = new Random(seed);
        HighScores = table ?? HighScoreTable.Load();
        SetUpRound();
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Ready;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public AttractPilot? Pilot { get; private set; }
    public bool Silent { get; set; }

    public int Score { get; private set; }
    public int Lives { get; private set; }
    public int Level { get; private set; } = 1;

    public List<Effect> Effects { get; } = [];

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls controls, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Step)
        {
            _accumulator -= Step;
            Tick(controls, start);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls c, bool start = false)
    {
        double dt = Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                if (startPressed) StartGame();
                else if (StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot!.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 3)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Level);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    public void StartGame()
    {
        Pilot = null;
        Begin(GameState.Playing);
    }

    private void StartAttract()
    {
        Pilot = new AttractPilot(_rng.Next());
        Begin(GameState.Attract);
    }

    private void Begin(GameState state)
    {
        State = state;
        StateTime = 0;
        Score = 0;
        Lives = StartingLives;
        _nextExtraLife = FirstExtraLife;
        NewEntryIndex = -1;
        Effects.Clear();
        Level = 1;
        Multiplier = 1;
        StartRound();
        Play(Sfx.StartTune);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        Pilot = null;
        _titleTunePending = true;
        Hum(false);
        Effects.Clear();
        Level = 1;
        SetUpRound();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Pilot = null;
        Hum(false);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Hum(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Playing) Hum(true);
    }

    private void GameOver()
    {
        Hum(false);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    internal void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtraLife)
        {
            _nextExtraLife += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    /// <summary>The qix's drone loops while it's on the move.</summary>
    private void Hum(bool on)
    {
        if (on == _hum) return;
        _hum = on;
        Play(on ? Sfx.Hum : Sfx.HumStop);
    }

    private void Popup(Vec at, string text, double life = 1.4) =>
        Effects.Add(new Effect { Kind = EffectKind.Score, Pos = at, Life = life, Text = text });

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}
