// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core;
using Multiplot.Plugins;

namespace Multiplot.Tests.Core;

/// <summary>Reference shifting used by Align Static (FR-UI-024a).</summary>
public class TimeReferenceTests
{
    private sealed class FakeFile(TimeTag refUtc, params FileEvent[] events) : IDataFile
    {
        public FakeFile(params FileEvent[] events) : this(new TimeTag(100, 0), events) { }
        public DataFileInfo Info { get; } = new() { RefUtc = refUtc };
        public IReadOnlyList<ParameterInfo> Parameters { get; } = Array.Empty<ParameterInfo>();
        public IReadOnlyList<FileEvent> Events { get; } = events;
        public string? CommentBlock => null;
        public RefreshResult Refresh(bool clearEventFlag) => default!;
        public ParameterData? Read(ParameterRequest request) => null;
        public int FindParameter(string name, out int nameList) { nameList = 0; return -1; }
        public void Dispose() { }
    }

    private static readonly TimeTag Ignition = new(100, 3_600_000, 250);
    private static readonly IReadOnlyList<IDataFile> Files = new[] { new FakeFile(new FileEvent("IGNITION", Ignition)) };

    [Theory]
    [InlineData("NONE", 12.5, -12.5)]
    [InlineData("", 12.5, -12.5)]
    [InlineData("START", -3, 3)]
    [InlineData("10", 2.25, 7.75)]
    [InlineData("-4", 0.000001, -4.000001)]
    public void ConstantReferencesBecomeUtcFromTheFileReference(string tref, double shift, double expectedOffset)
    {
        string? r = TimeReference.ShiftReference(Files, 0, tref, shift);
        var moved = TimeReference.GetRefUtc(Files, 0, r!);
        Assert.NotNull(moved);
        Assert.Equal(expectedOffset, TimeMath.Time(moved!.Value, Files[0].Info.RefUtc), 6);
    }

    [Theory]
    [InlineData("NONE", 12.5, "-12.5")]
    [InlineData("10", 2.25, "7.75")]
    [InlineData("-4", 0.000001, "-4.000001")]
    public void ConstantReferencesStayConstantWithoutACommonFileUtc(string tref, double shift, string expected)
    {
        IReadOnlyList<IDataFile>[] cases =
        [
            [new FakeFile(new TimeTag(0, 0))],
            [new FakeFile(new TimeTag(100, 0)), new FakeFile(new TimeTag(100, 5000))],
        ];
        foreach (var files in cases)
        {
            string? r = TimeReference.ShiftReference(files, 0, tref, shift);
            Assert.Equal(expected, r);
            Assert.Null(TimeReference.GetRefUtc(files, 0, r!));
            Assert.Equal(double.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), TimeReference.GetRefTime(files, 0, r!)!.Value, 9);
        }
    }

    [Theory]
    [InlineData("IGNITION", 1.5)]
    [InlineData("IGNITION", -7200.000123)]
    [InlineData("100/3600000.250", 0.0005)]
    [InlineData("100/01:00:00.000", 42)]
    public void UtcReferencesBecomeLiteralUtc(string tref, double shift)
    {
        var old = TimeReference.GetRefUtc(Files, 0, tref)!.Value;
        string? r = TimeReference.ShiftReference(Files, 0, tref, shift);
        Assert.NotNull(r);
        Assert.Contains('/', r);
        var moved = TimeReference.GetRefUtc(Files, 0, r)!.Value;
        // data time = utc − reference, so the new reference is `shift` seconds earlier
        Assert.Equal(shift, TimeMath.Time(old, moved), 6);
    }

    [Fact]
    public void UtcShiftCrossesMidnight()
    {
        var files = new[] { new FakeFile(new FileEvent("T0", new TimeTag(100, 100))) };
        var moved = TimeReference.GetRefUtc(files, 0, TimeReference.ShiftReference(files, 0, "T0", 1.0)!)!.Value;
        Assert.Equal(99, moved.JulianDay);
        Assert.Equal(86_399_100u, moved.MilliSeconds);
    }

    [Fact]
    public void EofCannotBeShifted() => Assert.Null(TimeReference.ShiftReference(Files, 0, "EOF", 1.0));

    [Theory]
    [InlineData("10", 2.0, "12")]
    [InlineData("-5.5", 0.25, "-5.25")]
    [InlineData("-3.4e+038", 2.0, "-3.4e+038")]
    [InlineData("+3.4e+038", 2.0, "+3.4e+038")]
    [InlineData("IGNITION+5", 2.0, "IGNITION+5")]
    [InlineData("EOF", 2.0, "EOF")]
    public void SliceTimesMoveOnlyWhenRelative(string time, double shift, string expected) =>
        Assert.Equal(expected, TimeReference.ShiftSliceTime(time, shift));
}
