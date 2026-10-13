using AVAQix.Models;

namespace AVAQix.Engine;

/// <summary>The player's marker: on the border, or out in the open drawing a stix.</summary>
public sealed class Marker
{
    public Pt At { get; set; }
    /// <summary>Which way it last moved.</summary>
    public Dir Heading { get; set; }
    /// <summary>Out drawing: the stix so far runs from <see cref="Stix"/>[0] (on the border) to here.</summary>
    public bool Drawing => Stix.Count > 0;
    public List<Pt> Stix { get; } = [];
    /// <summary>The stix has been drawn slowly all the way (it's worth double).</summary>
    public bool Slow { get; set; }
    /// <summary>Distance banked towards the next step.</summary>
    public double Move { get; set; }
    /// <summary>How long it has stood still while drawing.</summary>
    public double Stopped { get; set; }
    /// <summary>The fuse burning up the stix: how many steps along it, or −1 for none.</summary>
    public double Fuse { get; set; } = -1;
}

/// <summary>
/// The qix: a bundle of lines spinning and lurching about the open space. It's harmless to the marker
/// on the border, but touching a stix being drawn is the end of it.
/// </summary>
public sealed class Qix
{
    public Vec Centre { get; set; }
    public Vec Velocity { get; set; }
    public double Angle { get; set; }
    public double Spin { get; set; }
    public double Length { get; set; } = 10;
    public double Want { get; set; } = 14;
    /// <summary>Seconds until it changes its mind about where it's going.</summary>
    public double Restless { get; set; }
    /// <summary>The line as it is now, clipped to the open space.</summary>
    public Vec A { get; set; }
    public Vec B { get; set; }
    /// <summary>Its last few lines, newest first, for the shimmering trail.</summary>
    public List<(Vec A, Vec B)> Trail { get; } = [];
    public double Hue { get; set; }
}

/// <summary>A spark running round the border after the marker.</summary>
public sealed class Sparx
{
    public Pt At { get; set; }
    public Dir Heading { get; set; }
    /// <summary>Which way round: open space on its left (or its right).</summary>
    public bool OpenOnLeft { get; set; }
    /// <summary>Super sparx are quicker and can chase the marker up its stix.</summary>
    public bool Super { get; set; }
    /// <summary>Chasing up the stix: how far along it, or −1 when on the border.</summary>
    public int OnStix { get; set; } = -1;
    public double Move { get; set; }
    /// <summary>Just arriving: it fades in and can't hurt yet.</summary>
    public double Arrive { get; set; }
}

public enum EffectKind { Score, Burst, Flash }

public sealed class Effect
{
    public required EffectKind Kind { get; init; }
    public Vec Pos { get; set; }
    public double Age { get; set; }
    public double Life { get; init; } = 1;
    public string Text { get; init; } = "";
    public bool Done => Age >= Life;
}

/// <summary>
/// The controls for one step: the stick (each axis −1, 0 or 1) and the two draw buttons. A fast draw is
/// quick and worth the usual; a slow draw is half the speed and worth double.
/// </summary>
public record struct Controls(int Dx, int Dy, bool Fast = false, bool Slow = false)
{
    public bool Draw => Fast || Slow;
}
