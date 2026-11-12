// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using HelpBuilder;

namespace Multiplot.Tests.Core;

/// <summary>The Markdown user guide converts to HTML with every page link and #section link resolving.</summary>
public class UserGuideTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Guide_builds_without_broken_links()
    {
        string source = Path.Combine(Root, "docs", "user-guide");
        string output = Path.Combine(Path.GetTempPath(), "multiplot-guide-" + Guid.NewGuid().ToString("N"));
        try
        {
            IReadOnlyList<GuideProblem> problems = GuideBuilder.Build(source, output);
            Assert.True(problems.Count == 0, string.Join("\n", problems.Select(p => $"{p.File}({p.Line}): {p.Message}")));

            Assert.True(File.Exists(Path.Combine(output, "index.html")));
            Assert.True(File.Exists(Path.Combine(output, "guide.css")));
            foreach (string md in Directory.GetFiles(source, "*.md"))
                Assert.True(File.Exists(Path.Combine(output, GuideBuilder.OutputName(Path.GetFileName(md)))), md);
            Assert.Empty(Directory.GetFiles(output, "*.md"));

            string plot = File.ReadAllText(Path.Combine(output, "plot-windows.html"));
            Assert.Contains("id=\"colors-and-appearance\"", plot);
            Assert.Contains("<table>", plot);
            Assert.Contains("<code>&amp;&amp; ||</code>", File.ReadAllText(Path.Combine(output, "expressions.html")));
            Assert.DoesNotContain(".md\"", File.ReadAllText(Path.Combine(output, "index.html")));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public void Broken_links_are_reported()
    {
        string source = Path.Combine(Path.GetTempPath(), "multiplot-guide-src-" + Guid.NewGuid().ToString("N"));
        string output = source + "-out";
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "README.md"), "# Guide\n\n| Chapter |\n|---|\n| [A](a.md) |\n");
            File.WriteAllText(Path.Combine(source, "a.md"), "# A\n\n## Real section\n\n[ok](#real-section) [bad](#nope) [missing](b.md) [index](README.md)\n");

            IReadOnlyList<GuideProblem> problems = GuideBuilder.Build(source, output);

            Assert.Equal(2, problems.Count);
            Assert.Contains(problems, p => p.Message.Contains("#nope"));
            Assert.Contains(problems, p => p.Message.Contains("b.md"));
            string a = File.ReadAllText(Path.Combine(output, "a.html"));
            Assert.Contains("href=\"index.html\"", a);
            Assert.Contains("href=\"#real-section\"", a);
        }
        finally
        {
            Directory.Delete(source, true);
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }
}
