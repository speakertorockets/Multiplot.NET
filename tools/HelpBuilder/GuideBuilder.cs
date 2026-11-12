// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Net;
using System.Reflection;
using System.Text;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace HelpBuilder;

/// <summary>A broken link found while building the guide (1-based line and column).</summary>
public sealed record GuideProblem(string File, int Line, int Column, string Message);

/// <summary>
/// Converts the Markdown user guide to static HTML pages: README.md becomes index.html, every other
/// page keeps its name with an .html extension. Links between pages are rewritten to the .html names and
/// checked (target page and #section must exist). Every page gets the chapter list from the README table
/// as a sidebar, previous/next links and the shared guide.css.
/// </summary>
public static class GuideBuilder
{
    private const string IndexSource = "README.md";
    private const string IndexPage = "index.html";
    private const string StyleSheet = "guide.css";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseEmphasisExtras()
        .UseListExtras()
        .UseAutoLinks()
        .UseTaskLists()
        .UseFootnotes()
        .DisableHtml()
        .Build();

    private sealed record Page(string Source, string Output, MarkdownDocument Doc, string Title, HashSet<string> Ids);

    private sealed record NavEntry(string Output, string Label);

    /// <summary>Builds the HTML guide from <paramref name="sourceDir"/> into <paramref name="outputDir"/>.</summary>
    /// <returns>The broken links; empty when the guide is consistent.</returns>
    public static IReadOnlyList<GuideProblem> Build(string sourceDir, string outputDir)
    {
        sourceDir = Path.GetFullPath(sourceDir);
        outputDir = Path.GetFullPath(outputDir);
        var problems = new List<GuideProblem>();

        var pages = new Dictionary<string, Page>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(sourceDir, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file);
            MarkdownDocument doc = Markdown.Parse(File.ReadAllText(file), Pipeline);
            var ids = new HashSet<string>(doc.Descendants<HeadingBlock>().Select(h => h.GetAttributes().Id).OfType<string>(), StringComparer.Ordinal);
            string title = doc.Descendants<HeadingBlock>().FirstOrDefault(h => h.Level == 1) is { Inline: { } inl } ? InlineText(inl) : Path.GetFileNameWithoutExtension(name);
            pages[name] = new Page(name, OutputName(name), doc, title, ids);
        }
        if (!pages.TryGetValue(IndexSource, out Page? index))
            throw new FileNotFoundException("The user guide has no " + IndexSource, Path.Combine(sourceDir, IndexSource));

        // chapter order: README first, then the pages in the order the README links to them, then any others
        var nav = new List<NavEntry> { new(index.Output, "Contents") };
        foreach (LinkInline link in index.Doc.Descendants<LinkInline>())
        {
            if (SplitLocal(link.Url) is not (string path, _) || !pages.TryGetValue(path, out Page? p) || p == index) continue;
            if (nav.All(n => n.Output != p.Output)) nav.Add(new NavEntry(p.Output, InlineText(link)));
        }
        foreach (Page p in pages.Values.Where(p => nav.All(n => n.Output != p.Output)))
            nav.Add(new NavEntry(p.Output, p.Title));

        foreach (Page page in pages.Values)
        {
            RewriteLinks(page, pages, problems);
            UnescapeTablePipes(page.Doc);
        }

        Directory.CreateDirectory(outputDir);
        foreach (string stale in Directory.GetFiles(outputDir).Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
            File.Delete(stale);
        File.WriteAllText(Path.Combine(outputDir, StyleSheet), ReadStyleSheet(), Encoding.UTF8);
        foreach (Page page in pages.Values)
            File.WriteAllText(Path.Combine(outputDir, page.Output), RenderPage(page, nav), new UTF8Encoding(false));

        return problems;
    }

    /// <summary>Output file name for a source page.</summary>
    public static string OutputName(string source) =>
        source.Equals(IndexSource, StringComparison.OrdinalIgnoreCase) ? IndexPage : Path.ChangeExtension(source, ".html");

    /// <summary>Splits a link to another guide page into (page file, fragment); null for external or non-page links.</summary>
    private static (string? Path, string? Fragment)? SplitLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Contains(':') || url.StartsWith("//", StringComparison.Ordinal)) return null;
        int hash = url.IndexOf('#');
        string path = hash < 0 ? url : url[..hash];
        string? fragment = hash < 0 ? null : url[(hash + 1)..];
        if (path.Length == 0) return (null, fragment);                                       // #section on the same page
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;
        return (Uri.UnescapeDataString(path), fragment);
    }

    private static void RewriteLinks(Page page, Dictionary<string, Page> pages, List<GuideProblem> problems)
    {
        foreach (LinkInline link in page.Doc.Descendants<LinkInline>())
        {
            if (link.IsImage || SplitLocal(link.Url) is not ({ } local)) continue;
            Page? target = page;
            if (local.Path != null && (local.Path.Contains('/') || local.Path.Contains('\\') || !pages.TryGetValue(local.Path, out target)))
            {
                problems.Add(Problem(page, link, $"link to '{link.Url}': the page is not part of the user guide"));
                continue;
            }
            if (local.Fragment is { Length: > 0 } frag && !target!.Ids.Contains(frag))
                problems.Add(Problem(page, link, $"link to '{link.Url}': {target.Source} has no section '#{frag}'"));
            link.Url = (local.Path != null ? target!.Output : "") + (local.Fragment != null ? "#" + local.Fragment : "");
        }
    }

    /// <summary>
    /// In a table cell, <c>\|</c> inside a code span stands for a literal pipe (GitHub rule); Markdig keeps the backslash.
    /// </summary>
    private static void UnescapeTablePipes(MarkdownDocument doc)
    {
        foreach (Markdig.Extensions.Tables.TableCell cell in doc.Descendants<Markdig.Extensions.Tables.TableCell>())
            foreach (CodeInline code in cell.Descendants<CodeInline>())
                if (code.Content.Contains("\\|", StringComparison.Ordinal))
                    code.Content = code.Content.Replace("\\|", "|", StringComparison.Ordinal);
    }

    private static GuideProblem Problem(Page page, LinkInline link, string message) => new(page.Source, link.Line + 1, link.Column + 1, message);

    /// <summary>Plain text of inline content (headings and link labels).</summary>
    private static string InlineText(ContainerInline container)
    {
        var sb = new StringBuilder();
        foreach (Inline inline in container.Descendants<Inline>())
        {
            switch (inline)
            {
                case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
                case CodeInline code: sb.Append(code.Content); break;
                case LineBreakInline: sb.Append(' '); break;
            }
        }
        return sb.ToString().Trim();
    }

    private static string RenderPage(Page page, List<NavEntry> nav)
    {
        string body;
        using (var writer = new StringWriter())
        {
            var renderer = new HtmlRenderer(writer);
            Pipeline.Setup(renderer);
            renderer.Render(page.Doc);
            writer.Flush();
            body = writer.ToString();
        }

        static string E(string s) => WebUtility.HtmlEncode(s);
        int at = nav.FindIndex(n => n.Output == page.Output);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append("<title>").Append(E(page.Output == IndexPage ? page.Title : page.Title + " – Multiplot.NET User Guide")).Append("</title>\n")
          .Append("<link rel=\"stylesheet\" href=\"").Append(StyleSheet).Append("\">\n</head>\n<body>\n")
          .Append("<nav class=\"sidebar\">\n<a class=\"brand\" href=\"").Append(IndexPage).Append("\">Multiplot.NET User Guide</a>\n<ul>\n");
        foreach (NavEntry n in nav)
        {
            bool current = n.Output == page.Output;
            sb.Append("<li").Append(current ? " class=\"current\"" : "").Append("><a href=\"").Append(n.Output).Append("\"")
              .Append(current ? " aria-current=\"page\"" : "").Append('>').Append(E(n.Label)).Append("</a>");
            if (current && page.Output != IndexPage)
            {
                var sections = page.Doc.Descendants<HeadingBlock>().Where(h => h.Level == 2 && h.Inline != null && h.GetAttributes().Id != null).ToList();
                if (sections.Count > 0)
                {
                    sb.Append("\n<ul>\n");
                    foreach (HeadingBlock h in sections)
                        sb.Append("<li><a href=\"#").Append(E(h.GetAttributes().Id!)).Append("\">").Append(E(InlineText(h.Inline!))).Append("</a></li>\n");
                    sb.Append("</ul>\n");
                }
            }
            sb.Append("</li>\n");
        }
        sb.Append("</ul>\n</nav>\n<main>\n<article>\n").Append(body).Append("</article>\n<nav class=\"pager\">\n");
        if (at > 0) sb.Append("<a class=\"prev\" href=\"").Append(nav[at - 1].Output).Append("\">← ").Append(E(nav[at - 1].Label)).Append("</a>\n");
        if (at >= 0 && at < nav.Count - 1) sb.Append("<a class=\"next\" href=\"").Append(nav[at + 1].Output).Append("\">").Append(E(nav[at + 1].Label)).Append(" →</a>\n");
        sb.Append("</nav>\n</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string ReadStyleSheet()
    {
        using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HelpBuilder.guide.css")
            ?? throw new InvalidOperationException("guide.css resource missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
