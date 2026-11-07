// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using Multiplot.Core;

namespace Multiplot.Tests.Core;

/// <summary>File-qualified names used to limit a parameter selection to one file (FR-UI-020, issue #1).</summary>
public class FileQualifierTests
{
    private static readonly string[] Paths = { @"D:\live\rt_feed.sun", @"D:\archive\static_run.sun" };
    private static readonly string[] Groups = { "rt_feed", "static_run" };

    [Fact]
    public void FileNameSelectsOnlyThatFile()
    {
        Assert.Equal("static_run.sun", FileQualifier.For(Paths, Groups, 1));
        Assert.Equal("rt_feed.sun", FileQualifier.For(Paths, Groups, 0));
    }

    [Fact]
    public void SameFileNameFallsBackToThePath()
    {
        string[] paths = { @"D:\a\run.sun", @"D:\b\run.sun" };
        string[] groups = { "run", "run" };
        Assert.Equal(@"D:\b\run.sun", FileQualifier.For(paths, groups, 1));
    }

    [Fact]
    public void FileNameInsideAnotherPathOrGroupIsNotUsed()
    {
        string[] paths = { @"D:\x\a.sun", @"D:\x\data.sun" };
        string[] groups = { "a", "data" };
        Assert.Equal(@"D:\x\a.sun", FileQualifier.For(paths, groups, 0));
        // the other file's group name contains the file name
        string[] groups2 = { "a", "test a.sun copy" };
        Assert.Equal(@"D:\x\a.sun", FileQualifier.For(new[] { @"D:\x\a.sun", @"D:\y\b.sun" }, groups2, 0));
    }

    [Fact]
    public void ForbiddenCharactersUseTheLongestCleanPiece()
    {
        string[] paths = { @"D:\x\run (2), final.sun", @"D:\x\other.sun" };
        string[] groups = { "g1", "g2" };
        string? q = FileQualifier.For(paths, groups, 0);
        Assert.Equal("final.sun", q);
        Assert.Equal("final.sun:PC1", FileQualifier.Qualify("PC1", q!));
    }

    [Fact]
    public void NoUniqueTextGivesNull()
    {
        string[] paths = { @"D:\x\a.sun", @"D:\x\a.sun.bak" };
        string[] groups = { "", "" };
        Assert.Null(FileQualifier.For(paths, groups, 0));
        Assert.Equal(@"a.sun.bak", FileQualifier.For(paths, groups, 1));
    }

    [Theory]
    [InlineData("PC1", "PC1@*")]
    [InlineData("PC1,PC2", "PC1@*,PC2@*")]
    [InlineData("other:PC1,PC2", "other:PC1,PC2@*")]
    [InlineData("[PC1]-[PC2]", "[PC1]-[PC2]")]
    [InlineData("avg(PC1 0.5)", "avg(PC1 0.5)")]
    [InlineData("PC1\"Pressure: chamber\"", "PC1\"Pressure: chamber\"@*")]
    [InlineData("PC1@100:200", "PC1@100:200@*")]
    public void QualifyLeavesExpressionsAndQualifiedNamesAlone(string list, string expected)
    {
        // "@*" marks a qualified item in the expectation
        string got = FileQualifier.Qualify(list, "f.sun");
        string want = string.Join(",", expected.Split(',').Select(x => x.EndsWith("@*") ? "f.sun:" + x[..^2] : x));
        Assert.Equal(want, got);
    }

    [Theory]
    [InlineData("PC1", -1)]
    [InlineData("f.sun:PC1", 5)]
    [InlineData(@"D:\x\f.sun:PC1", 10)]
    [InlineData("PC1\"a:b\"", -1)]
    [InlineData("f.sun:PC1\"a:b\"", 5)]
    public void ColonIgnoresDescriptions(string name, int expected) => Assert.Equal(expected, FileQualifier.Colon(name));
}
