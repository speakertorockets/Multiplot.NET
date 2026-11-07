// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Plugins;

namespace Multiplot.Core;

/// <summary>
/// Numerically exact parameter operations: minimum/maximum, append, average, quantize,
/// interpolate and full-rate rebuild (FR-ACC-002, FR-FN-030, FR-SUN-076).
/// </summary>
public static class ParameterOps
{
    /// <summary>Time stamp modes of <see cref="Average"/>.</summary>
    public const int TsFirstPoint = 0, TsLastPoint = 1, TsAveraged = 2;

    private const double TimeTolerance = TimeMath.TimeTolerance;

    private static bool IsMultiTime(ParameterData p) => p.Source != null && SunSignatures.IsMultiTime(p.Source.Info.FileSig);

    // ------------------------------------------------------------------ min / max

    /// <summary>
    /// Minimum/maximum scan; with <paramref name="filterInvalid"/> NaN, ±Inf and
    /// subnormal float/double values (other than the dropout) are replaced by the dropout sentinel.
    /// Sets <see cref="ParameterData.MinIndex"/>/<see cref="ParameterData.MaxIndex"/> and the values.
    /// </summary>
    public static void ScanMinMax(ParameterData p, bool filterInvalid, bool updateValues = true)
    {
        p.MinIndex = DataMin(p, filterInvalid);
        p.MaxIndex = DataMax(p, filterInvalid);
        if (updateValues)
        {
            p.MinValue = ValueAt(p, p.MinIndex);
            p.MaxValue = ValueAt(p, p.MaxIndex);
        }
    }

    /// <summary>Value as double at index (0 points → dropout-converted default).</summary>
    public static double ValueAt(ParameterData p, int i)
    {
        if (i < 0 || p.Data.Length == 0 || i >= p.Data.Length) return 0;
        return ValueConvert.ToDouble(p.Data, p.Type, i);
    }

    /// <summary>Maximum value.</summary>
    public static int DataMax(ParameterData p, bool filterInvalid)
    {
        const int INVALID = -1;
        int offset = (p.Type == DataType.Float || p.Type == DataType.Double) ? INVALID : 0;
        int n = p.Points;
        switch (p.Type)
        {
            case DataType.Float:
                {
                    var d = (float[])p.Data;
                    for (int i = 0; i < n; i++)
                    {
                        float v = d[i];
                        bool drop = BitConverter.SingleToUInt32Bits(v) == DataTypes.DropOutBits;
                        if (!float.IsNormal(v) && v != 0f && !drop)
                        {
                            if (filterInvalid) d[i] = DataTypes.FpDropOut;
                        }
                        else if (!drop)
                        {
                            if (offset == INVALID || v > d[offset]) offset = i;
                        }
                    }
                    break;
                }
            case DataType.Double:
                {
                    var d = (double[])p.Data;
                    for (int i = 0; i < n; i++)
                    {
                        double v = d[i];
                        if (!double.IsNormal(v) && v != 0.0 && v != DataTypes.DpDropOut)
                        {
                            if (filterInvalid) d[i] = DataTypes.DpDropOut;
                        }
                        else if (v != DataTypes.DpDropOut)
                        {
                            if (offset == INVALID || v > d[offset]) offset = i;
                        }
                    }
                    break;
                }
            case DataType.ShortInt: { var d = (short[])p.Data; for (int i = 0; i < n; i++) if (d[i] > d[offset]) offset = i; break; }
            case DataType.LongInt: { var d = (int[])p.Data; for (int i = 0; i < n; i++) if (d[i] > d[offset]) offset = i; break; }
            case DataType.UShortInt: { var d = (ushort[])p.Data; for (int i = 0; i < n; i++) if (d[i] > d[offset]) offset = i; break; }
            case DataType.ULongInt: { var d = (uint[])p.Data; for (int i = 0; i < n; i++) if (d[i] > d[offset]) offset = i; break; }
            case DataType.Byte when p.Data is byte[] d: { for (int i = 0; i < n; i++) if (d[i] > d[offset]) offset = i; break; }
            case DataType.LongLong: { var d = (long[])p.Data; for (int i = 0; i < n; i++) if (d[i] > d[offset]) offset = i; break; }
            case DataType.ULongLong: { var d = (ulong[])p.Data; for (int i = 0; i < n; i++) if (d[i] > d[offset]) offset = i; break; }
            case DataType.TimeTag:
                {
                    var d = (TimeTag[])p.Data;
                    for (int i = 0; i < n; i++) if (TtKey(d[i]) > TtKey(d[offset])) offset = i;
                    break;
                }
        }
        return offset != INVALID ? offset : 0;
    }

    /// <summary>Minimum value.</summary>
    public static int DataMin(ParameterData p, bool filterInvalid)
    {
        const int INVALID = -1;
        int offset = (p.Type == DataType.Float || p.Type == DataType.Double) ? INVALID : 0;
        int n = p.Points;
        switch (p.Type)
        {
            case DataType.Float:
                {
                    var d = (float[])p.Data;
                    for (int i = 0; i < n; i++)
                    {
                        float v = d[i];
                        bool drop = BitConverter.SingleToUInt32Bits(v) == DataTypes.DropOutBits;
                        if (!float.IsNormal(v) && v != 0f && !drop)
                        {
                            if (filterInvalid) d[i] = DataTypes.FpDropOut;
                        }
                        else if (!drop)
                        {
                            if (offset == INVALID || v < d[offset]) offset = i;
                        }
                    }
                    break;
                }
            case DataType.Double:
                {
                    var d = (double[])p.Data;
                    for (int i = 0; i < n; i++)
                    {
                        double v = d[i];
                        if (!double.IsNormal(v) && v != 0.0 && v != DataTypes.DpDropOut)
                        {
                            if (filterInvalid) d[i] = DataTypes.DpDropOut;
                        }
                        else if (v != DataTypes.DpDropOut)
                        {
                            if (offset == INVALID || v < d[offset]) offset = i;
                        }
                    }
                    break;
                }
            case DataType.ShortInt: { var d = (short[])p.Data; for (int i = 0; i < n; i++) if (d[i] < d[offset]) offset = i; break; }
            case DataType.LongInt: { var d = (int[])p.Data; for (int i = 0; i < n; i++) if (d[i] < d[offset]) offset = i; break; }
            case DataType.UShortInt: { var d = (ushort[])p.Data; for (int i = 0; i < n; i++) if (d[i] < d[offset]) offset = i; break; }
            case DataType.ULongInt: { var d = (uint[])p.Data; for (int i = 0; i < n; i++) if (d[i] < d[offset]) offset = i; break; }
            case DataType.Byte when p.Data is byte[] d: { for (int i = 0; i < n; i++) if (d[i] < d[offset]) offset = i; break; }
            case DataType.LongLong: { var d = (long[])p.Data; for (int i = 0; i < n; i++) if (d[i] < d[offset]) offset = i; break; }
            case DataType.ULongLong: { var d = (ulong[])p.Data; for (int i = 0; i < n; i++) if (d[i] < d[offset]) offset = i; break; }
            case DataType.TimeTag:
                {
                    var d = (TimeTag[])p.Data;
                    for (int i = 0; i < n; i++) if (TtKey(d[i]) < TtKey(d[offset])) offset = i;
                    break;
                }
        }
        return offset != INVALID ? offset : 0;
    }

    private static ulong TtKey(TimeTag t) =>
        unchecked(((ulong)(long)t.JulianDay << 48) | ((ulong)t.MilliSeconds << 16) | t.MicroSeconds);

    // ------------------------------------------------------------------ append

    /// <summary>
    /// Appends the points of <paramref name="append"/> later than the last
    /// time of <paramref name="baseParm"/>. Returns the number of appended points, 0 when none,
    /// −2 on type mismatch.
    /// </summary>
    public static int Append(ParameterData baseParm, ParameterData append, bool retainScale)
    {
        bool utcPresent = false;
        if (!IsMultiTime(append))
        {
            baseParm.Utc = append.Utc;
            baseParm.UtcPoints = append.Utc != null ? append.UtcPoints + append.UtcOffset - baseParm.UtcOffset : 0;
        }
        if (IsMultiTime(baseParm) && IsMultiTime(append) && baseParm.Utc != null && append.Utc != null && baseParm.UtcPoints > 0 && append.UtcPoints > 0)
            utcPresent = true;

        if (baseParm.Type != append.Type) return -2;
        if (baseParm.Points == 0) return 0;

        bool validData = false;
        int startPoint = append.Points;
        for (int j = 0; j < startPoint; j++)
        {
            if (utcPresent)
            {
                if (TimeMath.Time(append.UtcAt(j), baseParm.UtcAt(baseParm.Points - 1)) > 0.0)
                {
                    startPoint = j;
                    validData = true;
                    break;
                }
            }
            else if (append.Time[j] > baseParm.Time[baseParm.Points - 1])
            {
                startPoint = j;
                validData = true;
                break;
            }
        }
        if (!validData) return 0;

        int combined = (append.Points - startPoint) + baseParm.Points;
        int addSpace = 0;
        bool baseCopy = false;
        if (baseParm.Time.Length < combined || baseParm.Data.Length < combined)
        {
            addSpace = combined < 131072 ? 131072 : (combined < 524288 ? (2 * combined) : 1048576);
            var nt = new float[combined + addSpace];
            Array.Copy(baseParm.Time, nt, baseParm.Points);
            var nd = ValueConvert.Allocate(baseParm.Type, combined + addSpace);
            Array.Copy(baseParm.Data, nd, baseParm.Points);
            baseParm.Time = nt;
            baseParm.Data = nd;
            baseCopy = true;
        }
        int nNew = append.Points - startPoint;
        Array.Copy(append.Data, startPoint, baseParm.Data, baseParm.Points, nNew);
        Array.Copy(append.Time, startPoint, baseParm.Time, baseParm.Points, nNew);

        float fDrop = DataTypes.FpDropOut;
        if (append.MinIndex >= startPoint)
        {
            if (append.MinValue < baseParm.MinValue && (float)append.MinValue != fDrop)
                baseParm.MinIndex = baseParm.Points + append.MinIndex - startPoint;
            else if ((float)baseParm.MinValue == fDrop)
            {
                baseParm.MinIndex = baseParm.Points + append.MinIndex - startPoint;
                baseParm.MinValue = ValueConvert.ToDouble(baseParm.Data, baseParm.Type, baseParm.MinIndex);
            }
        }
        if (append.MaxIndex >= startPoint)
        {
            if (append.MaxValue > baseParm.MaxValue && (float)append.MaxValue != fDrop)
                baseParm.MaxIndex = baseParm.Points + append.MaxIndex - startPoint;
            else if ((float)baseParm.MaxValue == fDrop)
            {
                baseParm.MaxIndex = baseParm.Points + append.MaxIndex - startPoint;
                baseParm.MaxValue = ValueConvert.ToDouble(baseParm.Data, baseParm.Type, baseParm.MaxIndex);
            }
        }
        if (!retainScale)
        {
            baseParm.MinValue = ValueConvert.ToDouble(baseParm.Data, baseParm.Type, baseParm.MinIndex);
            baseParm.MaxValue = ValueConvert.ToDouble(baseParm.Data, baseParm.Type, baseParm.MaxIndex);
        }

        if (IsMultiTime(baseParm) && IsMultiTime(append) && baseParm.Utc != null && append.Utc != null && baseParm.UtcPoints > 0 && append.UtcPoints > 0)
        {
            int gNew = append.UtcPoints - startPoint;
            if (gNew > 0)
            {
                if (baseCopy || baseParm.Utc.Length < baseParm.UtcPoints + gNew)
                {
                    var ng = new TimeTag[combined + Math.Max(addSpace, 0)];
                    Array.Copy(baseParm.Utc, baseParm.UtcOffset, ng, 0, baseParm.UtcPoints);
                    baseParm.Utc = ng;
                    baseParm.UtcOffset = 0;
                }
                Array.Copy(append.Utc, append.UtcOffset + startPoint, baseParm.Utc, baseParm.UtcOffset + baseParm.UtcPoints, gNew);
                baseParm.UtcPoints += gNew;
            }
        }

        baseParm.Points += nNew;
        return nNew;
    }

    // ------------------------------------------------------------------ average

    /// <summary>
    /// Block/sliding average or sampling,
    /// in place. Returns the new point count.
    /// </summary>
    public static int Average(ParameterData pid, float timeDelta, float avgTime, int timeStamp)
    {
        if (pid.Points <= 1) return pid.Points;

        float[] time = pid.Time;
        Array data = pid.Data;
        DataType type = pid.Type;

        if (timeStamp == TsAveraged)
        {
            float toff = avgTime / 2f;
            for (int q = 0; q < pid.Points; q++) time[q] -= toff;
        }

        int oCnt = pid.Points;
        timeDelta = Math.Abs(timeDelta);
        int slideMode;
        double[]? dBuf = null;
        float[]? tBuf = null;
        if (avgTime <= 0.0)
        {
            avgTime = timeDelta;
            slideMode = 2;
            timeStamp = TsFirstPoint;
        }
        else if (Math.Abs(avgTime - timeDelta) > TimeTolerance)
        {
            slideMode = 1;
            dBuf = new double[pid.Points];
            tBuf = new float[pid.Points];
        }
        else
            slideMode = 0;

        float tDelta = timeDelta > 1.0 ? 1.0f : timeDelta;
        double tBreak = ((double)(int)(time[0] / tDelta)) * (double)tDelta;
        switch (timeStamp)
        {
            case TsLastPoint:
            case TsAveraged:
                if ((tBreak + TimeTolerance) < (double)time[0]) tBreak += (double)timeDelta;
                break;
            case TsFirstPoint:
                if ((tBreak - TimeTolerance) <= (double)time[0]) tBreak += (double)timeDelta;
                break;
        }

        int startTimePoint = 0;
        double tStart = tBreak - avgTime;
        int aCnt = 0;
        int points = 0;
        int writeSample = 0;
        double tSum = 0, dSum = 0, dAvg;
        float t = 0;
        int p;
        int n = pid.Points;
        for (p = 0; p < n; p++)
        {
            if (p != 0)
            {
                switch (timeStamp)
                {
                    case TsLastPoint:
                        if (((double)time[p] - tBreak) > TimeTolerance) writeSample = 1;
                        break;
                    case TsFirstPoint:
                        if ((double)time[p] > (tBreak - TimeTolerance)) writeSample = 1;
                        break;
                    case TsAveraged:
                        if (((double)time[p] - tBreak) > TimeTolerance) writeSample = 1;
                        break;
                }
            }

            if (p == n - 1 && writeSample == 0)
            {
                writeSample = 2;
                if (slideMode == 0)
                {
                    if (!IsDropValue(data, type, p))
                    {
                        dSum += ValueConvert.ToDouble(data, type, p);
                        if (timeStamp == TsAveraged) tSum += (double)time[p];
                        points++;
                    }
                }
            }

            if (writeSample != 0)
            {
                if (slideMode != 0)
                {
                    if (slideMode == 2)
                    {
                        points = 1;
                        int src;
                        switch (timeStamp)
                        {
                            case TsLastPoint:
                                src = writeSample == 2 ? p : p - 1;
                                break;
                            default:
                                src = startTimePoint;
                                break;
                        }
                        dSum = ValueConvert.ToDouble(data, type, src);
                    }
                    else
                    {
                        points = 0;
                        dSum = tSum = 0.0;
                        int k;
                        for (k = p - 1; k >= 0; k--)
                        {
                            if (time[k] >= tStart)
                            {
                                if (type != DataType.TimeTag)
                                {
                                    if (!IsDropValue(data, type, k))
                                    {
                                        dSum += ValueConvert.ToDouble(data, type, k);
                                        if (timeStamp == TsAveraged) tSum += (double)time[k];
                                        points++;
                                    }
                                }
                                else
                                    points++;
                            }
                            else
                                break;
                        }
                        startTimePoint = k + 1;
                    }
                }

                if (points != 0)
                {
                    if (type == DataType.TimeTag)
                    {
                        var tt = (TimeTag[])data;
                        if (timeStamp == TsFirstPoint)
                        {
                            tt[aCnt] = tt[startTimePoint];
                            time[aCnt] = time[startTimePoint];
                        }
                        else
                        {
                            tt[aCnt] = tt[p - 1];
                            time[aCnt] = time[p - 1];
                        }
                    }
                    else
                    {
                        switch (timeStamp)
                        {
                            case TsLastPoint:
                                t = writeSample == 2 ? time[p] : time[p - 1];
                                break;
                            case TsFirstPoint:
                                t = time[startTimePoint];
                                break;
                            case TsAveraged:
                                t = (float)(tSum / (double)points);
                                break;
                        }
                        dAvg = dSum / (double)points;
                        if (slideMode == 1)
                        {
                            dBuf![aCnt] = dAvg;
                            tBuf![aCnt] = t;
                        }
                        else
                        {
                            time[aCnt] = t;
                            ValueConvert.SetFromDouble(data, type, aCnt, dAvg);
                        }

                        if (aCnt != 0)
                        {
                            if (dAvg < pid.MinValue) { pid.MinValue = dAvg; pid.MinIndex = aCnt; }
                            if (dAvg > pid.MaxValue) { pid.MaxValue = dAvg; pid.MaxIndex = aCnt; }
                        }
                        else
                        {
                            pid.MaxValue = pid.MinValue = dAvg;
                            pid.MinIndex = pid.MaxIndex = aCnt;
                        }

                        if (slideMode == 0 && writeSample != 2)
                        {
                            if (!IsDropValue(data, type, p))
                            {
                                dSum = ValueConvert.ToDouble(data, type, p);
                                points = 1;
                                switch (timeStamp)
                                {
                                    case TsAveraged: tSum = time[p]; break;
                                    case TsFirstPoint: startTimePoint = p; break;
                                }
                            }
                            else
                                points = 0;
                        }
                        else if (slideMode == 2)
                            startTimePoint = p;
                        else
                            points = 0;
                    }
                    aCnt++;
                }
                else
                {
                    if (slideMode == 0 && writeSample != 2)
                    {
                        if (!IsDropValue(data, type, p))
                        {
                            dSum = ValueConvert.ToDouble(data, type, p);
                            points = 1;
                            switch (timeStamp)
                            {
                                case TsAveraged: tSum = time[p]; break;
                                case TsFirstPoint: startTimePoint = p; break;
                            }
                        }
                        else
                            points = 0;
                    }
                    else if (slideMode == 2)
                        startTimePoint = p;
                    else
                        points = 0;
                }

                switch (timeStamp)
                {
                    case TsLastPoint:
                    case TsAveraged:
                        while ((tBreak + TimeTolerance) < (double)time[p]) tBreak += timeDelta;
                        break;
                    case TsFirstPoint:
                        while ((tBreak - TimeTolerance) <= (double)time[p]) tBreak += timeDelta;
                        break;
                }
                tStart = tBreak - avgTime;
                writeSample = 0;
            }
            else
            {
                if (slideMode == 0)
                {
                    if (!IsDropValue(data, type, p))
                    {
                        dSum += ValueConvert.ToDouble(data, type, p);
                        if (timeStamp == TsAveraged) tSum += (double)time[p];
                        points++;
                    }
                }
            }
        }

        if (points != 0 && slideMode == 0)
        {
            switch (timeStamp)
            {
                case TsLastPoint: t = time[p - 1]; break;
                case TsFirstPoint: t = time[startTimePoint]; break;
                case TsAveraged: t = (float)(tSum / (double)points); break;
            }
            dAvg = dSum / (double)points;
            time[aCnt] = t;
            ValueConvert.SetFromDouble(data, type, aCnt, dAvg);
            if (aCnt != 0)
            {
                if (dAvg < pid.MinValue) { pid.MinValue = dAvg; pid.MinIndex = aCnt; }
                if (dAvg > pid.MaxValue) { pid.MaxValue = dAvg; pid.MaxIndex = aCnt; }
            }
            else
            {
                pid.MaxValue = pid.MinValue = dAvg;
                pid.MinIndex = pid.MaxIndex = aCnt;
            }
            aCnt++;
        }

        pid.Points = aCnt;
        pid.UtcOffsetValid = false;
        if (slideMode == 1)
        {
            for (int q = 0; q < aCnt; q++)
            {
                time[q] = tBuf![q];
                ValueConvert.SetFromDouble(data, type, q, dBuf![q]);
            }
        }
        _ = oCnt;

        if (timeStamp == TsAveraged)
        {
            float toff = avgTime / 2f;
            for (int q = 0; q < pid.Points; q++) time[q] += toff;
        }
        return aCnt;
    }

    private static bool IsDropValue(Array data, DataType type, int i) => type switch
    {
        DataType.Float => BitConverter.SingleToUInt32Bits(((float[])data)[i]) == DataTypes.DropOutBits,
        DataType.Double => ((double[])data)[i] == DataTypes.DpDropOut,
        _ => false,
    };

    // ------------------------------------------------------------------ quantize / interpolate

    /// <summary>Fills <paramref name="dest"/> with values at <paramref name="timeBuf"/>.</summary>
    public static void Interpolate(float[] timeBuf, Array dest, int oPoints, ParameterData p)
    {
        for (int q = 0; q < oPoints; q++)
        {
            int tp = TimeMath.Location(timeBuf[q], p.Time, p.Points, 0);
            double dVal;
            if (((timeBuf[q] - p.Time[tp]) > TimeTolerance) && ((tp + 1) < p.Points))
                dVal = Interp(p.Time[tp], ValueConvert.ToDouble(p.Data, p.Type, tp), p.Time[tp + 1], ValueConvert.ToDouble(p.Data, p.Type, tp + 1), timeBuf[q]);
            else if (((p.Time[tp] - timeBuf[q]) > TimeTolerance) && tp != 0)
                dVal = Interp(p.Time[tp - 1], ValueConvert.ToDouble(p.Data, p.Type, tp - 1), p.Time[tp], ValueConvert.ToDouble(p.Data, p.Type, tp), timeBuf[q]);
            else
                dVal = ValueConvert.ToDouble(p.Data, p.Type, tp);
            ValueConvert.SetFromDouble(dest, p.Type, q, dVal);
        }
    }

    // INTERPOLATE(x1,y1,x2,y2,x) ((((y2-y1)*(x-x1))/(x2-x1))+y1) with float x arguments
    private static double Interp(float x1, double y1, float x2, double y2, float x)
    {
        float dx = x - x1;
        float span = x2 - x1;
        return (((y2 - y1) * dx) / span) + y1;
    }

    /// <summary>
    /// Resamples to multiples of
    /// <paramref name="timeDelta"/>. Returns the new point count (0 = failed, parameter unchanged).
    /// </summary>
    public static int Quantize(ParameterData p, double timeDelta, bool sample, bool filterInvalid = true)
    {
        string tid = DataTypes.ToId(p.Type);
        int tsz = tid[1] - '0';
        if (p.Points < 2 || timeDelta <= 0.0 || tsz < 1 || tsz > 8) return 0;

        double tStart = ((double)(int)(p.Time[0] / timeDelta)) * timeDelta;
        while (tStart < (double)p.Time[0]) tStart += timeDelta;
        double tStop = ((double)(int)(p.Time[p.Points - 1] / timeDelta)) * timeDelta;

        double tDelta = timeDelta > 1.0 ? 1.0 : timeDelta;
        double tBreak = ((double)(int)(tStart / tDelta)) * tDelta;

        int oPoints = (int)(uint)((tStop - tStart) / timeDelta) + 1;
        if (oPoints < 1) return 0;
        var dBuf = ValueConvert.Allocate(p.Type, oPoints + 1);
        var tBuf = new float[oPoints + 1];

        int i = 0;
        if (tBreak < (tStart - (tDelta * .25)))
        {
            tBuf[i++] = (float)tStart;
            oPoints++;
        }
        for (; i < oPoints; i++)
            tBuf[i] = (float)(tBreak + (i * timeDelta));

        if (sample)
        {
            for (int q = 0; q < oPoints; q++)
                Array.Copy(p.Data, TimeMath.Location(tBuf[q], p.Time, p.Points, 0), dBuf, q, 1);
        }
        else if ((p.Points >= (3 * oPoints)) && ((p.Rate > 0.0) && ((1.0 / p.Rate) < timeDelta)))
        {
            if (Average(p, (float)timeDelta, (float)timeDelta, TsAveraged) > 0)
            {
                for (int q = 0; q < oPoints; q++)
                    Array.Copy(p.Data, TimeMath.Location(tBuf[q], p.Time, p.Points, 0), dBuf, q, 1);
            }
            else
                oPoints = 0;
        }
        else
            Interpolate(tBuf, dBuf, oPoints, p);

        if (oPoints != 0)
        {
            p.Time = tBuf;
            p.Data = dBuf;
            p.Points = oPoints;
            p.UtcOffsetValid = false;
            p.MinIndex = DataMin(p, filterInvalid);
            p.MinValue = p.Data is float[] f0 ? f0[p.MinIndex] : ValueAt(p, p.MinIndex);
            p.MaxIndex = DataMax(p, filterInvalid);
            p.MaxValue = p.Data is float[] f1 ? f1[p.MaxIndex] : ValueAt(p, p.MaxIndex);
            p.SequenceNumber &= 0x0000ffff;
            p.Rate = 1.0f / (float)timeDelta;
        }
        return oPoints;
    }

    // ------------------------------------------------------------------ makeFullRate

    /// <summary>
    /// Rebuilds change-only float data to full rate (FR-SUN-076).
    /// Returns true when the parameter was expanded.
    /// </summary>
    public static bool MakeFullRate(ParameterData p, bool filterInvalid = true)
    {
        if (!(p.Rate > 0 && p.Points > 1 && p.Type == DataType.Float
              && ((p.Time[p.Points - 1] - p.Time[0]) * p.Rate) < 536870912.0))
            return false;

        double deltaT = 1.0 / (double)p.Rate;
        double timeStamp = p.Time[0];
        int oPnt, allPoints;
        for (oPnt = 1, allPoints = 1; oPnt < p.Points; allPoints++)
        {
            timeStamp += deltaT;
            if ((timeStamp - p.Time[oPnt]) > -deltaT)
                timeStamp = p.Time[oPnt++];
        }
        if (allPoints <= p.Points) return false;

        var timeBuf = new float[allPoints];
        var dataBuf = new float[allPoints];
        var src = (float[])p.Data;
        TimeTag[]? tags = null;
        bool hasTags = p.Utc != null && p.UtcPoints == p.Points && p.RefUtc.HasValue;
        if (hasTags) tags = new TimeTag[allPoints];

        timeStamp = timeBuf[0] = p.Time[0];
        float currValue = dataBuf[0] = src[0];
        if (tags != null) tags[0] = p.UtcAt(0);
        int nPnt;
        for (oPnt = 1, nPnt = 1; (nPnt < allPoints) && (oPnt < p.Points); nPnt++)
        {
            timeStamp += deltaT;
            if (tags != null) tags[nPnt] = TimeMath.Utc(timeStamp, p.RefUtc!.Value, true);
            if ((timeStamp - p.Time[oPnt]) > -deltaT)
            {
                timeStamp = p.Time[oPnt];
                currValue = src[oPnt];
                if (tags != null) tags[nPnt] = p.UtcAt(oPnt);
                oPnt++;
            }
            timeBuf[nPnt] = (float)timeStamp;
            dataBuf[nPnt] = currValue;
        }

        p.Time = timeBuf;
        p.Data = dataBuf;
        if (tags != null)
        {
            p.Utc = tags;
            p.UtcOffset = 0;
            p.UtcPoints = nPnt;
        }
        p.Points = nPnt;
        p.MinIndex = DataMin(p, filterInvalid);
        p.MaxIndex = DataMax(p, filterInvalid);
        p.SequenceNumber &= 0x0000ffff;
        return true;
    }

    // ------------------------------------------------------------------ compactData

    /// <summary>
    /// Compacts data (FR-RT-031): per interval keep min and max points in
    /// time order; unless <paramref name="compactAll"/> the newest 10 % of unpacked points are kept.
    /// Returns the new point count; <paramref name="newPackPosition"/> receives the packed position.
    /// </summary>
    public static int CompactData(ParameterData p, float interval, bool compactAll, int packPosition, bool filterInvalid, out int newPackPosition)
    {
        int compactedPoints = 0, packedPos = 0, startPos = 0;
        int n0 = p.Points;
        var time = p.Time;
        var data = p.Values;
        newPackPosition = packPosition;
        if (n0 == 0) return 0;
        int pointReserve = n0 - (compactAll ? 0 : ((n0 - packPosition) / 10));
        float endTime = time[0] + interval;
        for (int endPos = 1; endPos <= n0; endPos++)
        {
            if (endPos <= packPosition || endPos > pointReserve || endPos == n0 || (endPos < n0 && time[endPos] >= endTime))
            {
                int minPos, maxPos;
                switch (endPos - startPos)
                {
                    case 1: minPos = maxPos = startPos; break;
                    case 2: minPos = startPos; maxPos = minPos + 1; break;
                    default:
                        minPos = maxPos = startPos;
                        for (int k = startPos + 1; k < endPos; k++)
                        {
                            minPos = data[k] < data[minPos] ? k : minPos;
                            maxPos = data[k] > data[maxPos] ? k : maxPos;
                        }
                        break;
                }
                if (minPos > maxPos) (minPos, maxPos) = (maxPos, minPos);
                time[packedPos] = time[minPos];
                data[packedPos] = data[minPos];
                packedPos++;
                if (maxPos != minPos)
                {
                    time[packedPos] = time[maxPos];
                    data[packedPos] = data[maxPos];
                    packedPos++;
                }
                startPos = endPos;
                endTime = (startPos < time.Length ? time[startPos] : time[n0 - 1]) + interval;
                if (compactedPoints == 0 && endPos > pointReserve) compactedPoints = packedPos;
            }
        }
        p.Points = packedPos;
        p.UtcOffsetValid = false;
        p.MinIndex = DataMin(p, filterInvalid);
        p.MinValue = data[p.MinIndex];
        p.MaxIndex = DataMax(p, filterInvalid);
        p.MaxValue = data[p.MaxIndex];
        newPackPosition = compactedPoints != 0 ? compactedPoints : packedPos;
        return packedPos;
    }
}
