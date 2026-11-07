// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Core.Model;

/// <summary>Plot styles.</summary>
public static class PlotStyle
{
    /// <summary>2D line.</summary>
    public const int Line2D = CommandIds.ID_STYLE_2DLINE;
    /// <summary>2D scatter.</summary>
    public const int Scatter2D = CommandIds.ID_STYLE_2DSCATTER;
    /// <summary>3D line.</summary>
    public const int Line3D = CommandIds.ID_STYLE_3DLINE;
    /// <summary>3D area.</summary>
    public const int Area3D = CommandIds.ID_STYLE_3DAREA;
    /// <summary>Logic.</summary>
    public const int Logic = CommandIds.ID_STYLE_LOGIC;
    /// <summary>Tabular.</summary>
    public const int Tabular = CommandIds.ID_STYLE_TABULAR;
}

/// <summary>Data reduction modes (<see cref="PlotConfig.DataRate"/>).</summary>
public static class DataRateMode
{
    /// <summary>Full rate.</summary>
    public const int Full = CommandIds.ID_STYLE_DATARATE_FULL;
    /// <summary>Averaged.</summary>
    public const int Averaged = CommandIds.ID_STYLE_DATARATE_AVERAGED;
    /// <summary>Sampled.</summary>
    public const int Sampled = CommandIds.ID_STYLE_DATARATE_SAMPLED;
}

/// <summary>Multi-Y grouping.</summary>
public static class GroupBy
{
    /// <summary>One scale per curve.</summary>
    public const int Independent = CommandIds.ID_GROUPBY_INDEPENDENT;
    /// <summary>By name.</summary>
    public const int Name = CommandIds.ID_GROUPBY_NAME;
    /// <summary>By units.</summary>
    public const int Units = CommandIds.ID_GROUPBY_UNITS;
    /// <summary>By description.</summary>
    public const int Description = CommandIds.ID_GROUPBY_DESCRIPTION;
    /// <summary>By user defined scale id (\GROUP).</summary>
    public const int UserDefined = CommandIds.ID_GROUPBY_UDEF;
}

/// <summary>Window caption sources.</summary>
public static class CaptionSource
{
    /// <summary>X title.</summary>
    public const int XTitle = CommandIds.ID_CAPTION_XTITLE;
    /// <summary>Y title.</summary>
    public const int YTitle = CommandIds.ID_CAPTION_YTITLE;
    /// <summary>Plot number.</summary>
    public const int PlotNo = CommandIds.ID_CAPTION_PLOTNO;
    /// <summary>Parameter set.</summary>
    public const int ParmSet = CommandIds.ID_CAPTION_PARMSET;
    /// <summary>User text.</summary>
    public const int User = CommandIds.ID_CAPTION_USER;
    /// <summary>Script name.</summary>
    public const int Script = CommandIds.ID_CAPTION_SCRIPT;
}

/// <summary>Nomenclature item flags and indexes.</summary>
public static class Nomen
{
    /// <summary>Flag bits.</summary>
    public const int NAME = 0x0001, UNITS = 0x0002, DESC = 0x0004, REFT = 0x0008, UDEF = 0x0010, FILENAME = 0x0020,
        SEQNUM = 0x0040, EFORMAT = 0x0080, ALTNAME = 0x0100, DBNAME = 0x0200, EXTNAME = 0x0400, CURVAL = 0x0800, CURTIME = 0x1000;

    /// <summary>Item indexes.</summary>
    public const int I_DBNAME = 0, I_FILENAME = 1, I_NAME = 2, I_UNITS = 3, I_DESC = 4, I_REFT = 5, I_UDEF = 6, I_ALTNAME = 7,
        I_EXTNAME = 8, I_SEQNUM = 9, I_CURVAL = 10, I_CURTIME = 11, ITEMS = 12;

    /// <summary>Default display order.</summary>
    public static readonly int[] DefaultOrder = { DBNAME, FILENAME, NAME, UNITS, DESC, REFT, UDEF, ALTNAME, EXTNAME, SEQNUM, CURVAL, CURTIME };

    /// <summary>Index of a flag in the item list.</summary>
    public static int IndexOf(int flag) => Array.IndexOf(DefaultOrder, flag);
}

/// <summary>Colour table offsets.</summary>
public static class ColorIndex
{
    /// <summary>Curve colours 0..99.</summary>
    public const int MaxColors = 100;
    /// <summary>Grid.</summary>
    public const int Grid = 100;
    /// <summary>Label.</summary>
    public const int Label = 101;
    /// <summary>Background.</summary>
    public const int Background = 102;
    /// <summary>Monochrome.</summary>
    public const int Mono = 103;
    /// <summary>Title.</summary>
    public const int Title = 104;
    /// <summary>Event markers (line and text).</summary>
    public const int Marker = 105;
    /// <summary>Redline markers (line and text).</summary>
    public const int Redline = 106;
    /// <summary>Total.</summary>
    public const int Total = 107;

    /// <summary>Default colour table (COLORREF 0x00BBGGRR).</summary>
    public static int[] Defaults()
    {
        var c = new int[Total];
        var cycle = PlotColors.Palette;
        for (int i = 0; i < MaxColors; i++) c[i] = cycle[i % cycle.Length];
        c[Grid] = c[Label] = c[Title] = 0x808080;
        c[Background] = 0xFFFFFF;
        c[Mono] = 0;
        c[Marker] = 0x808080;
        c[Redline] = 0x3C14DC; // crimson RGB(220,20,60): not a curve palette colour
        return c;
    }
}

/// <summary>Windows LOGFONT subset used for plot fonts.</summary>
public sealed class LogFont
{
    /// <summary>Height (negative = character height).</summary>
    public int Height = -13;
    /// <summary>Width.</summary>
    public int Width;
    /// <summary>Escapement.</summary>
    public int Escapement;
    /// <summary>Orientation.</summary>
    public int Orientation;
    /// <summary>Weight (400 normal, 700 bold).</summary>
    public int Weight = 700;
    /// <summary>Italic.</summary>
    public byte Italic;
    /// <summary>Underline.</summary>
    public byte Underline;
    /// <summary>Strike out.</summary>
    public byte StrikeOut;
    /// <summary>Character set.</summary>
    public byte CharSet;
    /// <summary>Output precision.</summary>
    public byte OutPrecision = 3;
    /// <summary>Clip precision.</summary>
    public byte ClipPrecision = 2;
    /// <summary>Quality.</summary>
    public byte Quality = 1;
    /// <summary>Pitch and family.</summary>
    public byte PitchAndFamily = 34;
    /// <summary>Face name.</summary>
    public string FaceName = "Arial";

    /// <summary>Copy.</summary>
    public LogFont Clone() => (LogFont)MemberwiseClone();

    /// <summary>Default: zeroed LOGFONT with the given height and face "Multiplot".</summary>
    public static LogFont DefaultFace(int height) => new()
    {
        Height = height, Weight = 0, OutPrecision = 0, ClipPrecision = 0, Quality = 0, PitchAndFamily = 0, FaceName = "Multiplot",
    };
}

/// <summary>Per plot configuration.</summary>
public sealed class PlotConfig
{
    public bool Symbols = true, Multi, Color = true, PidVsPid, EndToEnd, Legend = true, LegendShift;
    /// <summary>Test Info: the test title of the plot's first file in the bottom-left corner.</summary>
    public bool TestInfo;
    public bool ClockTime, GridLines = true, BoldFrame, StairStep, LogX, LogY, MinorTics, MarkersTopmost, ShowFileEvents = true;
    /// <summary>Auto scale the time (X) axis: the axis spans all data and follows new real-time data.</summary>
    public bool AutoScaleX = true;
    /// <summary>Auto scale the value (Y) axes: they fit the data inside the visible time range.</summary>
    public bool AutoScaleY = true;
    public bool DropoutIgnore = true, DropoutMarker, LegendColor, GroupId, DefTabTime = true, Use64BitTime;
    public uint SymFreq = 8, SamFreq = 100, AvgFreq = 100, ZaxFreq = 25;
    public int Style = PlotStyle.Line2D, DataRate = DataRateMode.Full, ZAngle = 45, GridStyle = 2;
    public int LineSize = 1, LineSize3D = 1, TitleSize = 2, SymbolSize;
    public int XNomen = 3, YNomen = 39, LegendColumns = 2, CaptionSource = Model.CaptionSource.ParmSet;
    public int[] XNomOrder = (int[])Nomen.DefaultOrder.Clone(), YNomOrder = (int[])Nomen.DefaultOrder.Clone();
    public int[] XNomLen = new int[Nomen.ITEMS], YNomLen = new int[Nomen.ITEMS];
    public LogFont LabelFont = LogFont.DefaultFace(30), TitleFont = LogFont.DefaultFace(45);
    /// <summary>Label (legend/scale) and title font sizes in tenths of a point (FR-UI-029).</summary>
    public int LabelPointSize = 100, TitlePointSize = 120;
    public int[] DrawColor = ColorIndex.Defaults();

    /// <summary>True for 3D styles.</summary>
    public bool Is3D => Style == PlotStyle.Line3D || Style == PlotStyle.Area3D;

    /// <summary>Deep copy (the plot owns an independent colour table).</summary>
    public PlotConfig Clone()
    {
        var c = (PlotConfig)MemberwiseClone();
        c.XNomOrder = (int[])XNomOrder.Clone();
        c.YNomOrder = (int[])YNomOrder.Clone();
        c.XNomLen = (int[])XNomLen.Clone();
        c.YNomLen = (int[])YNomLen.Clone();
        c.LabelFont = LabelFont.Clone();
        c.TitleFont = TitleFont.Clone();
        c.DrawColor = (int[])DrawColor.Clone();
        return c;
    }
}
