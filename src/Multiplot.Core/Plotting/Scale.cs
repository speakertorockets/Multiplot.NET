namespace Multiplot.Core.Plotting;

/// <summary>
/// Axis scaling, FR-PLT-010..014.
/// Outputs (min, max, graduation, divisions, decimals) must stay numerically stable.
/// </summary>
public sealed class Scale
{
    /// <summary>Use a fixed graduation (input).</summary>
    public bool FixedScale;
    /// <summary>Scale persists across new data (input).</summary>
    public bool Persist;
    /// <summary>Exponential label format (input).</summary>
    public bool EFormat;
    /// <summary>Logarithmic scale (input).</summary>
    public bool Logarithmic;
    /// <summary>Range minimum (input/output; natural log for log scales).</summary>
    public double RMin;
    /// <summary>Range maximum (input/output).</summary>
    public double RMax;
    /// <summary>Screen extent in pixels (input).</summary>
    public double ScreenRange;
    /// <summary>Graduation (input when fixed).</summary>
    public double Graduation = 1.0;
    /// <summary>Pixels per unit (output).</summary>
    public double Factor;
    /// <summary>Range (output).</summary>
    public double Range = 1.0;
    /// <summary>Number of divisions (output).</summary>
    public int Divisions;
    /// <summary>Decimal places of labels (output).</summary>
    public int DecimalPlaces;
    /// <summary>Log scale decade exponents (output).</summary>
    public int MinExp, MaxExp;
    /// <summary>Scale (group) name for multi-Y.</summary>
    public string Name = "";
    /// <summary>Number of curves using the scale.</summary>
    public int UseCount;

    private int _zoomLevel;
    private double[,] _zoomStack = new double[2, 20];
    private bool[] _persistStack = new bool[20];

    /// <summary>Default constructor.</summary>
    public Scale()
    {
        RMin = 0.0;
        RMax = 1.0;
        Range = 1.0;
        ScreenRange = 10.0;
    }

    /// <summary>Initialises and computes the scale.</summary>
    public Scale(double min, double max, double screenRange, bool eFormat, bool logarithmic, bool fixedScale, bool persist)
    {
        RMin = min;
        RMax = max;
        Range = RMax - RMin;
        ScreenRange = screenRange;
        EFormat = eFormat;
        Logarithmic = logarithmic;
        FixedScale = fixedScale;
        Persist = persist;
        Graduation = 1.0;
        GetScale();
    }

    /// <summary>Copy.</summary>
    public Scale Clone()
    {
        var s = (Scale)MemberwiseClone();
        s._zoomStack = (double[,])_zoomStack.Clone();
        s._persistStack = (bool[])_persistStack.Clone();
        return s;
    }

    private static double Log(double x) => x > 0.0 ? Math.Log(x) : -99999.99;

    /// <summary>C <c>(long)double</c> on MSVC x64 (out of range → INT_MIN).</summary>
    private static int CLong(double v)
    {
        if (double.IsNaN(v) || v >= 2147483648.0 || v <= -2147483649.0) return int.MinValue;
        return (int)v;
    }

    /// <summary>C <c>(__int64)double</c> on MSVC x64.</summary>
    private static long CInt64(double v)
    {
        if (double.IsNaN(v) || v >= 9.2233720368547758E18 || v < -9.2233720368547758E18) return long.MinValue;
        return (long)v;
    }

    /// <summary>Number of significant decimal places.</summary>
    public static int FDigits(double value)
    {
        int nDigits = 9;
        string buf = Crt.Sprintf("%.*f", nDigits, value);
        int len = buf.Length;
        while (len > 0 && buf[len - 1] == '0')
        {
            len--;
            nDigits--;
        }
        return nDigits;
    }

    /// <summary>Number of significant exponential places.</summary>
    public static int EDigits(double value)
    {
        int nDigits = 9;
        do
        {
            string buf = Crt.Sprintf("%.*e", nDigits, value);
            if (!buf.Contains("0e", StringComparison.Ordinal)) break;
        } while (--nDigits > 1);
        return nDigits;
    }

    private void ScaleFactor()
    {
        double x = Math.Ceiling(Range / 7);
        int i = CLong(x);
        double pwr;
        for (pwr = 0.0; i >= 10; pwr++) i /= 10;
        pwr = Math.Pow(10.0, pwr);
        bool search = true;
        while (search)
        {
            for (i = 0; search && i < 5; i++)
            {
                if (i == 2) continue;
                if (x <= ((double)(i + 1) * pwr))
                {
                    search = false;
                    Graduation = (double)(i + 1) * pwr;
                    break;
                }
            }
            if (search) pwr *= 10.0;
            if (double.IsInfinity(pwr)) { Graduation = x; break; }
        }
    }

    private int Fraction()
    {
        // sprintf(buffer, "%e", m_range) into a 14 byte buffer
        string s = Crt.Sprintf("%e", Range);
        char[] buffer = new char[14];
        for (int k = 0; k < Math.Min(13, s.Length); k++) buffer[k] = s[k];
        int i = Crt.Atol(new string(buffer, 10, 4).TrimEnd('\0'));
        buffer[1] = buffer[2];
        buffer[2] = buffer[3];
        buffer[3] = '\0';
        Range = Crt.Atof(new string(buffer, 0, 3).TrimEnd('\0'));
        i += 2;
        return CLong(Math.Pow(10.0, i));
    }

    private void ScaleLimits()
    {
        double mxdiff, mndiff;
        if (CLong(Graduation) != 0)
        {
            long g = CInt64(Graduation);
            mndiff = g == 0 ? 0 : (double)(CInt64(RMin) % g);
            mxdiff = g == 0 ? 0 : (double)(CInt64(RMax) % g);
        }
        else
        {
            mndiff = RMin - Math.Truncate(RMin);
            mxdiff = RMax - Math.Truncate(RMax);
            if (double.IsInfinity(RMin)) mndiff = 0;
            if (double.IsInfinity(RMax)) mxdiff = 0;
        }

        if (mndiff != 0)
        {
            if (RMin < 0.0) RMin -= (Graduation + mndiff);
            else RMin -= mndiff;
        }
        if (mxdiff != 0)
        {
            if (RMax > 0.0) RMax += (Graduation - mxdiff);
            else RMax -= mxdiff;
        }
    }

    /// <summary>Computes the scale.</summary>
    public void GetScale(bool clock = false)
    {
        double baseMin, baseMax, temp;
        int power = 1, irmin = 0;
        double[] smScale = { .2, .25, .25 };

        if (!clock && !FixedScale)
        {
            RMin = Crt.RoundSignificant(RMin, 7);
            RMax = Crt.RoundSignificant(RMax, 7);
        }
        if (RMin > RMax)
        {
            temp = RMin;
            RMin = RMax;
            RMax = temp;
        }

        if (Logarithmic)
        {
            LogScale();
            return;
        }

        baseMin = RMin;
        baseMax = RMax;
        if (baseMin == 0.0 && baseMax == 1.0)
        {
            baseMin -= .1;
            baseMax += .1;
        }
        Range = RMax - RMin;

        if (FixedScale && Graduation > 0.0)
        {
            Divisions = (int)((Range / (Graduation > 0.0 ? Graduation : 1.0)) + .5);
            if ((((double)Divisions * Graduation) - Range) < -0.0001) Divisions++;
            RMax = (Divisions * Graduation) + RMin;
            Range = RMax - RMin;
            Factor = ScreenRange / Range;
        }
        else
        {
            if (Range < 3.4e-37)
            {
                RMax += .1;
                RMin -= .1;
                Range = .199;
            }
            if (Range > .8)
            {
                RMin = Math.Floor(RMin);
                RMax = Math.Ceiling(RMax);
            }
            else
            {
                irmin = CLong(RMin);
                RMin -= irmin;
                RMax -= irmin;
                power = Fraction();
                RMin = Math.Ceiling(RMin * power);
                RMax = Math.Floor(RMax * power);
            }

            if (Range <= 3.0)
            {
                temp = RMax - RMin;
                int i = (int)Math.Ceiling(temp);
                if (i > 0) i--;
                if (i > 2) i = 2;
                if (i < 0) i = 0;
                Graduation = smScale[i];
            }
            else
                ScaleFactor();

            ScaleLimits();
            RMin = (RMin / power) + irmin;
            RMax = (RMax / power) + irmin;
            Graduation /= power;

            if (Range > 3.4e-10)
            {
                int nTries = 0;
                while (RMax < baseMax)
                {
                    RMax += Graduation;
                    if ((RMin + Graduation) <= baseMin) RMin += Graduation;
                    if (++nTries > 2) break;
                }
                nTries = 0;
                while (RMin > baseMin)
                {
                    RMin -= Graduation;
                    if ((RMax - Graduation) > baseMax) RMax -= Graduation;
                    if (++nTries > 2) break;
                }
            }

            Range = RMax - RMin;
            Factor = ScreenRange / Range;
            Divisions = (int)((Range / Graduation) + .5);
            if (Divisions >= 8 && (Divisions % 2) == 0)
            {
                Divisions /= 2;
                Graduation *= 2.0;
            }
            else if (Divisions < 2)
            {
                Divisions *= 4;
                Graduation /= 4.0;
            }
        }

        if (EFormat)
            DecimalPlaces = Math.Max(EDigits(RMin + Graduation), EDigits(RMax + Graduation));
        else
            DecimalPlaces = Math.Max(FDigits(Graduation), FDigits(RMin));
    }

    /// <summary>Logarithmic scale.</summary>
    public void LogScale()
    {
        int n;
        if (RMin <= 0.0) RMin = .001;
        if (RMax <= 0.0) RMax = 1.0;
        for (n = -16; n < 17; n++)
        {
            double x = Math.Pow(10.0, n);
            if (RMin < x) break;
        }
        MinExp = n - 1;
        RMin = Math.Pow(10.0, MinExp);
        for (n = MinExp + 1; n < 17; n++)
        {
            double x = Math.Pow(10.0, n);
            if (RMax <= x) break;
        }
        MaxExp = n;
        RMax = Math.Pow(10.0, n);

        Range = Log(RMax) - Log(RMin);
        Factor = ScreenRange / Range;
        Divisions = MaxExp - MinExp;
        Graduation = Range / Divisions;

        if (EFormat)
            DecimalPlaces = Math.Max(EDigits(RMin), EDigits(RMax));
        else
            DecimalPlaces = MinExp < 0 ? Math.Abs(MinExp) : 0;

        RMax = Log(RMax);
        RMin = Log(RMin);
        if (DecimalPlaces > 7) DecimalPlaces = 7;
    }

    /// <summary>Converts a C GMT format to user tokens.</summary>
    public static string MakeUserFormat(string fmt) => fmt
        .Replace("%03u/", "DDD/").Replace("%02u:%02u:%02u", "HR:MN:SC").Replace("%02u:%02u", "HR:MN")
        .Replace("%02u", "HR").Replace(".%03u", ".MIL").Replace("%03u", "DDD");

    /// <summary>Converts user tokens to a C GMT format.</summary>
    public static string MakeCFormat(string fmt) => fmt
        .Replace("DDD/", "%03u/").Replace("HR", "%02u").Replace("MN", "%02u").Replace("SC", "%02u")
        .Replace(".MIL", ".%03u").Replace("DDD", "%03u");

    /// <summary>Adjusts range and graduation to clock boundaries.</summary>
    public void AdjustToClockScale(uint mTod, string formatStr)
    {
        double[] gradTable = { 3, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 21600, 43200, 86400, 432000, 864000, 2592000, 15552000 };
        double[] rangeTable = { .01, .1, 1, 10, 60, 300, 1800, 3600, 18000, 86400, 345600, 3e38 };
        uint[] intervalTable = { 1, 50, 500, 1000, 5000, 60000, 300000, 600000, 900000, 3600000, 43200000, 86400000 };
        int[] minPrecTable = { 1, 1, 1, 2, 2, 3, 3, 3, 3, 3, 4, 5 };
        string fmt = MakeUserFormat(formatStr);
        int minPrecision;
        if (fmt.Contains("MIL")) minPrecision = 1;
        else if (fmt.Contains("SC")) minPrecision = 2;
        else if (fmt.Contains("MN")) minPrecision = 3;
        else if (fmt.Contains("HR")) minPrecision = 4;
        else if (fmt.Contains("DDD")) minPrecision = 5;
        else minPrecision = 0;

        int i;
        for (i = 0; i < rangeTable.Length; i++)
            if (Range < rangeTable[i] && minPrecision <= minPrecTable[i]) break;
        double timeShift = (double)(mTod % intervalTable[i < rangeTable.Length ? i : rangeTable.Length - 1]) / 1000.0;

        double fixMin = RMin - timeShift;
        RMin = fixMin;
        GetScale(true);
        RMin = fixMin;

        for (i = 0; i < gradTable.Length; i++)
            if (Graduation <= gradTable[i]) break;
        if (i != 0 && i < gradTable.Length)
        {
            Graduation = gradTable[i];
            FixedScale = true;
            GetScale(true);
            FixedScale = false;
        }
    }

    /// <summary>Zoom stack depth.</summary>
    public int IsZoomed => _zoomLevel;

    private void ShiftZoomStack()
    {
        for (int i = 1; i < _zoomLevel; i++)
        {
            _zoomStack[0, i - 1] = _zoomStack[0, i];
            _zoomStack[1, i - 1] = _zoomStack[1, i];
            _persistStack[i - 1] = _persistStack[i];
        }
        _zoomLevel--;
    }

    /// <summary>Pushes a zoom level (oldest dropped at 20).</summary>
    public void AddZoomLevel(double min, double max, bool persist)
    {
        if (_zoomLevel >= 20) ShiftZoomStack();
        _zoomStack[0, _zoomLevel] = min;
        _zoomStack[1, _zoomLevel] = max;
        _persistStack[_zoomLevel] = persist;
        _zoomLevel++;
    }

    /// <summary>Pops a zoom level; on an empty stack returns 0..1, not persistent.</summary>
    public int GetZoomLevel(out double min, out double max, out bool persist)
    {
        if (_zoomLevel != 0)
        {
            _zoomLevel--;
            min = _zoomStack[0, _zoomLevel];
            max = _zoomStack[1, _zoomLevel];
            persist = _persistStack[_zoomLevel];
        }
        else
        {
            min = 0.0;
            max = 1.0;
            persist = false;
        }
        return _zoomLevel;
    }

    /// <summary>Clears the zoom stack.</summary>
    public void ResetZoomLevel() => _zoomLevel = 0;
}
