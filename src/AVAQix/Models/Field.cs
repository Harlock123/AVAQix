namespace AVAQix.Models;

/// <summary>A 2D point or vector in grid units (y grows downwards).</summary>
public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public override string ToString() => $"({X:0.#}, {Y:0.#})";
}

public enum Dir { None, Up, Down, Left, Right }

public static class Dirs
{
    public static readonly Dir[] All = [Dir.Up, Dir.Down, Dir.Left, Dir.Right];

    public static int Dx(this Dir d) => d switch { Dir.Left => -1, Dir.Right => 1, _ => 0 };
    public static int Dy(this Dir d) => d switch { Dir.Up => -1, Dir.Down => 1, _ => 0 };

    public static Dir Opposite(this Dir d) => d switch
    {
        Dir.Up => Dir.Down, Dir.Down => Dir.Up, Dir.Left => Dir.Right, Dir.Right => Dir.Left, _ => Dir.None,
    };

    /// <summary>A quarter turn anticlockwise on screen: facing right, your left is up.</summary>
    public static Dir TurnLeft(this Dir d) => d switch
    {
        Dir.Right => Dir.Up, Dir.Up => Dir.Left, Dir.Left => Dir.Down, Dir.Down => Dir.Right, _ => Dir.None,
    };

    public static Dir TurnRight(this Dir d) => d.TurnLeft().Opposite();
}

/// <summary>A corner of the grid: where lines run and the marker and the sparx stand.</summary>
public readonly record struct Pt(int X, int Y)
{
    public Pt Step(Dir d) => new(X + d.Dx(), Y + d.Dy());
    public Vec Vec => new(X, Y);
    public int Manhattan(Pt o) => Math.Abs(X - o.X) + Math.Abs(Y - o.Y);
    public override string ToString() => $"[{X},{Y}]";
}

/// <summary>What a cell of the playfield is.</summary>
public enum Claim : byte { Open, Fast, Slow }

/// <summary>
/// The playfield: a grid of cells, each still open or claimed (by a fast or a slow draw). Lines run
/// between the cells, along the corners. A line between an open cell and a claimed one (the edge of
/// the field counts as claimed) is a <i>border</i>: that's where the marker and the sparx go. A line
/// with open cells both sides is open space, where the marker can draw.
/// </summary>
public sealed class Playfield
{
    public const int Width = 160;
    public const int Height = 108;
    public const int CellCount = Width * Height;

    private readonly Claim[] _cells = new Claim[CellCount];

    /// <summary>How many cells are claimed.</summary>
    public int ClaimedCount { get; private set; }

    /// <summary>Goes up every time cells change: the screen redraws its picture of the field then.</summary>
    public int Version { get; private set; }

    public double Percent => ClaimedCount * 100.0 / CellCount;

    public static bool InCells(int cx, int cy) => cx is >= 0 and < Width && cy is >= 0 and < Height;
    public static bool InPoints(Pt p) => p.X is >= 0 and <= Width && p.Y is >= 0 and <= Height;

    public Claim this[int cx, int cy] => _cells[cy * Width + cx];

    /// <summary>Claimed, or off the field.</summary>
    public bool Closed(int cx, int cy) => !InCells(cx, cy) || _cells[cy * Width + cx] != Claim.Open;

    public bool OpenAt(Vec v) => v.X >= 0 && v.Y >= 0 && !Closed((int)v.X, (int)v.Y);

    /// <summary>The cells on the left and right of the line from p one step d (facing along it).</summary>
    public static ((int X, int Y) Left, (int X, int Y) Right) Sides(Pt p, Dir d) => d switch
    {
        Dir.Right => ((p.X, p.Y - 1), (p.X, p.Y)),
        Dir.Left => ((p.X - 1, p.Y), (p.X - 1, p.Y - 1)),
        Dir.Down => ((p.X, p.Y), (p.X - 1, p.Y)),
        _ => ((p.X - 1, p.Y - 1), (p.X, p.Y - 1)),
    };

    /// <summary>The line from p one step d is a border: claimed one side, open the other.</summary>
    public bool IsBorder(Pt p, Dir d)
    {
        if (!InPoints(p) || !InPoints(p.Step(d))) return false;
        var (l, r) = Sides(p, d);
        return Closed(l.X, l.Y) != Closed(r.X, r.Y);
    }

    /// <summary>The line from p one step d runs through open space.</summary>
    public bool IsOpen(Pt p, Dir d)
    {
        if (!InPoints(p) || !InPoints(p.Step(d))) return false;
        var (l, r) = Sides(p, d);
        return !Closed(l.X, l.Y) && !Closed(r.X, r.Y);
    }

    /// <summary>A border runs through this corner.</summary>
    public bool OnBorder(Pt p) => Dirs.All.Any(d => IsBorder(p, d));

    /// <summary>
    /// The way on along the border from p, coming in heading d, keeping the open side on the left (or the
    /// right). Turns towards the open side first, so it hugs it; None if there's no border here.
    /// </summary>
    public Dir Follow(Pt p, Dir d, bool openOnLeft)
    {
        Dir[] order = d == Dir.None ? Dirs.All
            : openOnLeft ? [d.TurnLeft(), d, d.TurnRight(), d.Opposite()]
            : [d.TurnRight(), d, d.TurnLeft(), d.Opposite()];
        foreach (var c in order)
        {
            if (!IsBorder(p, c)) continue;
            var (l, r) = Sides(p, c);
            bool leftOpen = !Closed(l.X, l.Y);
            if (leftOpen == openOnLeft) return c;
        }
        return Dir.None;
    }

    /// <summary>The border corner nearest to a point (for putting the sparx back after a claim).</summary>
    public Pt NearestBorder(Pt from)
    {
        if (OnBorder(from)) return from;
        Pt best = from;
        int bestD = int.MaxValue;
        for (int y = 0; y <= Height; y++)
            for (int x = 0; x <= Width; x++)
            {
                var p = new Pt(x, y);
                int d = p.Manhattan(from);
                if (d >= bestD || !OnBorder(p)) continue;
                bestD = d;
                best = p;
            }
        return best;
    }

    /// <summary>The first border corner straight down from the top of a column (or up from the bottom).</summary>
    public Pt BorderInColumn(int x, bool fromTop)
    {
        for (int i = 0; i <= Height; i++)
        {
            var p = new Pt(x, fromTop ? i : Height - i);
            if (OnBorder(p)) return p;
        }
        return NearestBorder(new Pt(x, fromTop ? 0 : Height));
    }

    /// <summary>
    /// Closes a stix (a line drawn through open space from border to border): every open area it cuts
    /// off that holds no qix becomes claimed. Returns the cells claimed, and whether the qixes ended up
    /// on different sides (split).
    /// </summary>
    public (int Claimed, bool Split) Close(IReadOnlyList<Pt> stix, IEnumerable<Vec> qixes, Claim kind)
    {
        // The stix as walls between cells: hWall[x, y] blocks between (x, y−1) and (x, y);
        // vWall[x, y] blocks between (x−1, y) and (x, y).
        var hWall = new bool[Width, Height + 1];
        var vWall = new bool[Width + 1, Height];
        for (int i = 1; i < stix.Count; i++)
        {
            Pt a = stix[i - 1], b = stix[i];
            if (a.Y == b.Y) hWall[Math.Min(a.X, b.X), a.Y] = true;
            else vWall[a.X, Math.Min(a.Y, b.Y)] = true;
        }

        var region = new int[CellCount];
        int regions = 0;
        var queue = new Queue<int>();
        for (int start = 0; start < CellCount; start++)
        {
            if (region[start] != 0 || _cells[start] != Claim.Open) continue;
            regions++;
            region[start] = regions;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % Width, y = i / Width;
                void Visit(int nx, int ny)
                {
                    int n = ny * Width + nx;
                    if (region[n] != 0 || _cells[n] != Claim.Open) return;
                    region[n] = regions;
                    queue.Enqueue(n);
                }
                if (x > 0 && !vWall[x, y]) Visit(x - 1, y);
                if (x < Width - 1 && !vWall[x + 1, y]) Visit(x + 1, y);
                if (y > 0 && !hWall[x, y]) Visit(x, y - 1);
                if (y < Height - 1 && !hWall[x, y + 1]) Visit(x, y + 1);
            }
        }

        var keep = new HashSet<int>();
        foreach (var q in qixes)
        {
            int cx = Math.Clamp((int)q.X, 0, Width - 1), cy = Math.Clamp((int)q.Y, 0, Height - 1);
            int r = region[cy * Width + cx];
            if (r != 0) keep.Add(r);
        }
        int claimed = 0;
        for (int i = 0; i < CellCount; i++)
        {
            if (region[i] == 0 || keep.Contains(region[i])) continue;
            _cells[i] = kind;
            claimed++;
        }
        ClaimedCount += claimed;
        if (claimed > 0) Version++;
        return (claimed, keep.Count > 1);
    }

    /// <summary>Claims a block of cells directly (for the tests and screenshots).</summary>
    internal void ClaimBox(int x0, int y0, int x1, int y1, Claim kind)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!InCells(x, y) || _cells[y * Width + x] != Claim.Open) continue;
                _cells[y * Width + x] = kind;
                ClaimedCount++;
            }
        Version++;
    }
}
