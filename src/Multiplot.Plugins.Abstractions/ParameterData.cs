// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Plugins;

/// <summary>
/// Retrieved parameter data: nomenclature, relative time array,
/// typed data buffer, optional GMT array, reference used, min/max and rate.
/// Arrays may be longer than <see cref="Points"/> (pre-allocated capacity).
/// </summary>
public sealed class ParameterData
{
    /// <summary>The file the data was read from.</summary>
    public IDataFile? Source { get; set; }
    /// <summary>Curve/parameter name (mutable copy).</summary>
    public string Name { get; set; } = " ";
    /// <summary>Units.</summary>
    public string Units { get; set; } = " ";
    /// <summary>Description.</summary>
    public string Description { get; set; } = " ";
    /// <summary>Data type of <see cref="Data"/>.</summary>
    public DataType Type { get; set; } = DataType.Float;
    /// <summary>Number of valid points.</summary>
    public int Points { get; set; }
    /// <summary>Relative time in seconds (float32).</summary>
    public float[] Time { get; set; } = Array.Empty<float>();
    /// <summary>
    /// Typed data: float[], double[], short[], int[], ushort[], uint[], long[], ulong[], byte[] or TimeTag[].
    /// </summary>
    public Array Data { get; set; } = Array.Empty<float>();
    /// <summary>Optional GMT per point; element <c>i</c> is at <c>Gmt[GmtOffset + i]</c>.</summary>
    public TimeTag[]? Gmt { get; set; }
    /// <summary>Offset of point 0 in <see cref="Gmt"/> (common-time file index sharing).</summary>
    public int GmtOffset { get; set; }
    /// <summary>Number of valid GMT entries starting at <see cref="GmtOffset"/>.</summary>
    public int GmtPoints { get; set; }
    /// <summary>False once a reduction made the GMT array invalid.</summary>
    public bool GmtOffsetValid { get; set; } = true;
    /// <summary>Point offset in the file index (common-time formats).</summary>
    public int IndexStart { get; set; }
    /// <summary>Relative reference offset used for the read, if any.</summary>
    public double? RefTime { get; set; }
    /// <summary>Reference GMT used for the read, if any.</summary>
    public TimeTag? RefGmt { get; set; }
    /// <summary>Index of the minimum value.</summary>
    public int MinIndex { get; set; }
    /// <summary>Index of the maximum value.</summary>
    public int MaxIndex { get; set; }
    /// <summary>Minimum value.</summary>
    public double MinValue { get; set; }
    /// <summary>Maximum value.</summary>
    public double MaxValue { get; set; }
    /// <summary>Sequence number.</summary>
    public uint SequenceNumber { get; set; }
    /// <summary>Data rate (−1 unknown, 0 variable).</summary>
    public float Rate { get; set; } = -1f;
    /// <summary>Time shift applied by the alignment tool.</summary>
    public float TimeShift { get; set; }
    /// <summary>Map position in the source file.</summary>
    public int MapPosition { get; set; }
    /// <summary>Time stamp nomenclature name.</summary>
    public string TimeName { get; set; } = "Time";
    /// <summary>Time stamp nomenclature units.</summary>
    public string TimeUnits { get; set; } = "Seconds";
    /// <summary>Time stamp nomenclature description.</summary>
    public string TimeDescription { get; set; } = "Relative Time in Seconds";
    /// <summary>The source is a common-time format whose time stamps come from the file index.</summary>
    public bool CommonTimeIndex { get; set; }
    /// <summary>Application slot: redline type set by <c>redline()</c> (0 none, 1 upper, 2 lower, 3 generic).</summary>
    public int Redline { get; set; }
    /// <summary>Non-fatal warning code of the read (0 = none).</summary>
    public int Warning { get; set; }

    /// <summary>Float data (when <see cref="Type"/> is Float).</summary>
    public float[] Values => (float[])Data;

    /// <summary>True when a valid GMT array covers all points.</summary>
    public bool HasValidGmt => Gmt != null && GmtPoints >= Points && RefGmt.HasValue && GmtOffsetValid;

    /// <summary>GMT of point <paramref name="i"/> (requires <see cref="Gmt"/>).</summary>
    public TimeTag GmtAt(int i) => Gmt![GmtOffset + i];

    /// <summary>Value of point <paramref name="i"/> converted to double.</summary>
    public double GetDouble(int i) => ValueConvert.ToDouble(Data, Type, i);

    /// <summary>True when point <paramref name="i"/> is a dropout (bit pattern for floats).</summary>
    public bool IsDropOut(int i) => Type switch
    {
        DataType.Float => DataTypes.IsDropOut(((float[])Data)[i]),
        DataType.Double => DataTypes.IsDropOut(((double[])Data)[i]),
        _ => false,
    };

    /// <summary>Deep copy.</summary>
    public ParameterData Clone()
    {
        var c = (ParameterData)MemberwiseClone();
        c.Time = (float[])Time.Clone();
        c.Data = (Array)Data.Clone();
        if (Gmt != null && !CommonTimeIndex) c.Gmt = (TimeTag[])Gmt.Clone();
        return c;
    }

    /// <summary>Creates a float parameter with the given arrays.</summary>
    public static ParameterData FromFloat(string name, float[] time, float[] values, int points)
    {
        var p = new ParameterData { Name = name, Type = DataType.Float, Time = time, Data = values, Points = points };
        return p;
    }
}

/// <summary>Element conversion helpers (to float / to double).</summary>
public static class ValueConvert
{
    /// <summary>Double value of element <paramref name="i"/>.</summary>
    public static double ToDouble(Array data, DataType type, int i) => type switch
    {
        DataType.Float => ((float[])data)[i],
        DataType.Double => ((double[])data)[i],
        DataType.ShortInt => ((short[])data)[i],
        DataType.LongInt => ((int[])data)[i],
        DataType.UShortInt => ((ushort[])data)[i],
        DataType.ULongInt => ((uint[])data)[i],
        DataType.LongLong => ((long[])data)[i],
        DataType.ULongLong => ((ulong[])data)[i],
        DataType.Byte => ((byte[])data)[i],
        DataType.TimeTag => TimeTagToDouble(((TimeTag[])data)[i]),
        _ => DataTypes.DpDropOut, // DT_VARIABLE falls through to default
    };

    /// <summary>Float value of element <paramref name="i"/>.</summary>
    public static float ToFloat(Array data, DataType type, int i) => type switch
    {
        DataType.Float => ((float[])data)[i],
        DataType.Double => (float)((double[])data)[i],
        DataType.ShortInt => ((short[])data)[i],
        DataType.LongInt => ((int[])data)[i],
        DataType.UShortInt => ((ushort[])data)[i],
        DataType.ULongInt => ((uint[])data)[i],
        DataType.LongLong => ((long[])data)[i],
        DataType.ULongLong => ((ulong[])data)[i],
        DataType.Byte => ((byte[])data)[i],
        DataType.TimeTag => TimeTagToFloat(((TimeTag[])data)[i]),
        DataType.Variable => ((float[])data)[i],
        _ => DataTypes.FpDropOut,
    };

    /// <summary>TimeTag to float: milliseconds of year (day loop in float).</summary>
    public static float TimeTagToFloat(TimeTag t)
    {
        float x = t.MilliSeconds + (t.MicroSeconds / 1000f);
        for (short d = 0; d < t.JulianDay; d++) x += 86400000.0f;
        return x;
    }

    /// <summary>TimeTag to double.</summary>
    public static double TimeTagToDouble(TimeTag t)
    {
        double x = t.MilliSeconds + (t.MicroSeconds / 1000.0);
        for (short d = 0; d < t.JulianDay; d++) x += 86400000.0;
        return x;
    }

    /// <summary>Creates a typed array of the given type and length.</summary>
    public static Array Allocate(DataType type, int length) => type switch
    {
        DataType.Float => new float[length],
        DataType.Double => new double[length],
        DataType.ShortInt => new short[length],
        DataType.LongInt => new int[length],
        DataType.UShortInt => new ushort[length],
        DataType.ULongInt => new uint[length],
        DataType.LongLong => new long[length],
        DataType.ULongLong => new ulong[length],
        DataType.TimeTag => new TimeTag[length],
        DataType.Variable => new float[length],
        _ => new byte[length],
    };

    /// <summary>Sets element <paramref name="i"/> from a double.</summary>
    public static void SetFromDouble(Array data, DataType type, int i, double v)
    {
        switch (type)
        {
            case DataType.Float: ((float[])data)[i] = (float)v; break;
            case DataType.Double: ((double[])data)[i] = v; break;
            case DataType.ShortInt: ((short[])data)[i] = unchecked((short)CTrunc(v)); break;
            case DataType.LongInt: ((int[])data)[i] = unchecked((int)CTrunc(v)); break;
            case DataType.UShortInt: ((ushort[])data)[i] = unchecked((ushort)CTrunc(v)); break;
            case DataType.ULongInt: ((uint[])data)[i] = unchecked((uint)CTrunc(v)); break;
            case DataType.LongLong: ((long[])data)[i] = CTrunc(v); break;
            case DataType.ULongLong: ((ulong[])data)[i] = v >= 9.2233720368547758E18 ? unchecked((ulong)CTrunc(v - 9.2233720368547758E18) + 9223372036854775808UL) : unchecked((ulong)CTrunc(v)); break;
            case DataType.Byte: ((byte[])data)[i] = unchecked((byte)CTrunc(v)); break;
            case DataType.TimeTag:
                ((TimeTag[])data)[i] = new TimeTag((short)(v / 86400000.0f), (uint)CTrunc(v % 86400000.0), 0);
                break;
        }
    }

    /// <summary>C style truncation of a double to int64 (MSVC x64 semantics; NaN/out of range → int64.MinValue).</summary>
    public static long CTrunc(double v)
    {
        if (double.IsNaN(v) || v >= 9.2233720368547758E18 || v < -9.2233720368547758E18) return long.MinValue;
        return (long)v;
    }
}
