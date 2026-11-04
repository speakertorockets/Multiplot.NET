namespace Multiplot.Core.Model;

/// <summary>Marker types.</summary>
public static class MarkerType
{
    public const int Text = 0, Line = 1, Box = 2, Circle = 3, Event = 4, Redline = 5, Image = 6, Max = 7;

    /// <summary>Marker type names.</summary>
    public static readonly string[] Names = { "TEXT", "LINE", "BOX", "CIRCLE", "EVENT", "REDLINE", "IMAGE" };
}

/// <summary>Plot marker (FR-MRK-001).</summary>
public sealed class Marker
{
    /// <summary>Maximum markers per plot.</summary>
    public const int MaxMarkers = 1000;

    public bool Valid;
    public float X1, X2, Y1, Y2;
    public int Color = ColorIndex.Grid;
    public int Type;
    public string Label = "";
    /// <summary>Loaded image name (IMAGE markers), cache key for the renderer.</summary>
    public string BitmapName = "";
    /// <summary>Renderer image cache (opaque).</summary>
    public object? Image;

    /// <summary>Sets the marker.</summary>
    public void Set(float xBegin, float yBegin, float xEnd, float yEnd, int nColor, int mType, string? mLabel)
    {
        X1 = xBegin; X2 = xEnd; Y1 = yBegin; Y2 = yEnd;
        Color = nColor >= 0 && nColor <= ColorIndex.Background ? nColor : ColorIndex.Grid;
        Type = mType;
        Label = (mLabel ?? "").Trim(' ');
        Valid = true;
    }

    /// <summary>Clears the marker.</summary>
    public void Clear()
    {
        Valid = false;
        X1 = X2 = Y1 = Y2 = 0f;
        Label = "";
        Color = ColorIndex.Grid;
        Type = 0;
        Image = null;
        BitmapName = "";
    }

    /// <summary>Marks the marker as unset.</summary>
    public void UnSet() => Valid = false;

    /// <summary>Copy (image cache shared).</summary>
    public Marker Clone() => (Marker)MemberwiseClone();

    /// <summary>True when the markers differ.</summary>
    public bool DiffersFrom(Marker m) => m.Valid != Valid || m.X1 != X1 || m.Y1 != Y1 || m.X2 != X2 || m.Y2 != Y2 || m.Color != Color || m.Type != Type || m.Label != Label;

    /// <summary>Creates an array of empty markers.</summary>
    public static Marker[] NewSet()
    {
        var a = new Marker[MaxMarkers];
        for (int i = 0; i < a.Length; i++) a[i] = new Marker();
        return a;
    }

    /// <summary>Deep copies a marker set.</summary>
    public static Marker[] CopySet(Marker[] src)
    {
        var a = new Marker[MaxMarkers];
        for (int i = 0; i < a.Length; i++) a[i] = src[i].Clone();
        return a;
    }
}
