using System.Text;

namespace Multiplot.Core;

/// <summary>
/// Text I/O conventions (NFR-COMP-001): Windows-1252 by default,
/// UTF-8 with BOM accepted on input.
/// </summary>
public static class AnsiText
{
    private static readonly Lazy<Encoding> AnsiLazy = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    });

    /// <summary>The Windows-1252 encoding.</summary>
    public static Encoding Ansi => AnsiLazy.Value;

    /// <summary>Decodes bytes: UTF-8 if a BOM is present, else Windows-1252.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Ansi.GetString(bytes);
    }

    /// <summary>Decodes a byte range as Windows-1252.</summary>
    public static string DecodeAnsi(ReadOnlySpan<byte> bytes) => Ansi.GetString(bytes);

    /// <summary>Encodes text as Windows-1252.</summary>
    public static byte[] Encode(string s) => Ansi.GetBytes(s);

    /// <summary>Reads a whole text file with these encoding rules (shared read).</summary>
    public static string ReadAllText(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return Decode(ms.ToArray());
    }

    /// <summary>Reads lines (CR/LF or LF)</summary>
    public static List<string> ReadAllLines(string path)
    {
        string text = ReadAllText(path);
        return SplitLines(text);
    }

    /// <summary>Splits text into lines like <c>fgets</c> with trailing CR/LF removed.</summary>
    public static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                int end = i;
                if (end > start && text[end - 1] == '\r') end--;
                lines.Add(text.Substring(start, end - start));
                start = i + 1;
            }
        }
        if (start < text.Length)
        {
            string last = text[start..];
            if (last.EndsWith('\r')) last = last[..^1];
            lines.Add(last);
        }
        return lines;
    }

    /// <summary>Creates a writer with Windows-1252 encoding and CRLF line endings.</summary>
    public static StreamWriter CreateWriter(string path, bool append = false)
    {
        var w = new StreamWriter(path, append, Ansi) { NewLine = "\r\n" };
        return w;
    }

    /// <summary>Writes text with Windows-1252 encoding.</summary>
    public static void WriteAllText(string path, string text) => File.WriteAllBytes(path, Encode(text));
}
