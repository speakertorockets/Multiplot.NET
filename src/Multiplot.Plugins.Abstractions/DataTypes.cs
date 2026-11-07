// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

namespace Multiplot.Plugins;

/// <summary>Data type ids (<c>DT_*</c>).</summary>
public enum DataType : ushort
{
    /// <summary>Raw image, no conversion.</summary>
    Image = 0,
    /// <summary>IEEE float32 ("f4").</summary>
    Float = 1,
    /// <summary>IEEE float64 ("d8").</summary>
    Double = 2,
    /// <summary>int16 ("i2").</summary>
    ShortInt = 3,
    /// <summary>int32 ("i4").</summary>
    LongInt = 4,
    /// <summary>uint16 ("u2").</summary>
    UShortInt = 5,
    /// <summary>uint32 ("u4").</summary>
    ULongInt = 6,
    /// <summary>TimeTag ("t8").</summary>
    TimeTag = 7,
    /// <summary>byte ("b1").</summary>
    Byte = 8,
    /// <summary>packed time ("p4").</summary>
    PackedTime = 9,
    /// <summary>variable block object ("v1").</summary>
    Variable = 10,
    /// <summary>uint64 ("u8").</summary>
    ULongLong = 11,
    /// <summary>int64 ("i8").</summary>
    LongLong = 12,
    /// <summary>Pseudo type used to parse clock strings.</summary>
    ClockString = 15,
}

/// <summary>Helpers for type id strings, sizes and sentinel values.</summary>
public static class DataTypes
{
    /// <summary>Flag OR-ed into a requested type to also return a TimeTag array (MULTITIME only).</summary>
    public const ushort ReturnUtcFlag = 0x0100;

    /// <summary>Dropout bit pattern (4 byte types).</summary>
    public const uint DropOutBits = 0x80000001;

    /// <summary>Variable-block placeholder bit pattern.</summary>
    public const uint VariableBits = 0x00000001;

    /// <summary>The dropout sentinel as float32 (negative subnormal −1.40130e−45).</summary>
    public static readonly float FpDropOut = BitConverter.UInt32BitsToSingle(DropOutBits);

    /// <summary>The variable-block placeholder as float32.</summary>
    public static readonly float FpVariable = BitConverter.UInt32BitsToSingle(VariableBits);

    /// <summary>The dropout sentinel as double (<c>(double)FP_DROP_OUT</c>).</summary>
    public static readonly double DpDropOut = (double)BitConverter.UInt32BitsToSingle(DropOutBits);

    /// <summary>True when the float has the dropout bit pattern (never compare the value).</summary>
    public static bool IsDropOut(float value) => BitConverter.SingleToUInt32Bits(value) == DropOutBits;

    /// <summary>True when the double equals the dropout sentinel converted to double.</summary>
    public static bool IsDropOut(double value) => value == DpDropOut;

    /// <summary>Data type from its type string.</summary>
    public static DataType FromId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return DataType.Image;
        char c1 = id.Length > 1 ? id[1] : '\0';
        switch (id[0])
        {
            case 'f': return DataType.Float;
            case 'd': return DataType.Double;
            case 'i': return c1 == '2' ? DataType.ShortInt : c1 == '4' ? DataType.LongInt : c1 == '8' ? DataType.LongLong : DataType.Image;
            case 'u': return c1 == '2' ? DataType.UShortInt : c1 == '4' ? DataType.ULongInt : c1 == '8' ? DataType.ULongLong : DataType.Image;
            case 't': return DataType.TimeTag;
            case 'b': return DataType.Byte;
            case 'v': return DataType.Variable;
            default: return DataType.Image; // 'p' is not mapped (p4 data reads as DT_IMAGE)
        }
    }

    /// <summary>Element size from a type id string.</summary>
    public static int SizeFromId(string id)
    {
        int s = id.Length > 1 ? id[1] - '0' : 4;
        return s is >= 1 and <= 8 ? s : 4;
    }


    /// <summary>Type string of a data type.</summary>
    public static string ToId(DataType type) => type switch
    {
        DataType.Float => "f4",
        DataType.Double => "d8",
        DataType.ShortInt => "i2",
        DataType.LongInt => "i4",
        DataType.UShortInt => "u2",
        DataType.ULongInt => "u4",
        DataType.LongLong => "i8",
        DataType.ULongLong => "u8",
        DataType.TimeTag => "t8",
        DataType.Byte => "b1",
        DataType.Variable => "v1",
        DataType.PackedTime => "p4",
        _ => "  ",
    };

    /// <summary>Element size in bytes (id string digit; image = 4).</summary>
    public static int SizeOf(DataType type) => type switch
    {
        DataType.Float => 4,
        DataType.Double => 8,
        DataType.ShortInt => 2,
        DataType.LongInt => 4,
        DataType.UShortInt => 2,
        DataType.ULongInt => 4,
        DataType.LongLong => 8,
        DataType.ULongLong => 8,
        DataType.TimeTag => 8,
        DataType.Byte => 1,
        DataType.Variable => 1,
        DataType.PackedTime => 4,
        _ => 4,
    };
}
