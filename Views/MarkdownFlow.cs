using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
// Disambiguate WPF types from the WinForms/System.Drawing globals.
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace Effortless.Views;

/// <summary>
/// Converts between markdown text and a WPF <see cref="FlowDocument"/> for the
/// rich scratch-pad editor. Scope is deliberately small so plain text always
/// round-trips losslessly:
///   **bold**  *italic*  ==highlight==  &lt;mark style="background:#hex"&gt;…&lt;/mark&gt;
///   - [ ] task   - [x] done
/// Tasks are a leading text glyph (☐/☑) tagged "taskglyph", NOT an embedded
/// control — embedded controls don't receive clicks inside an editable
/// RichTextBox. One source line ↔ one paragraph.
/// </summary>
internal static class MarkdownFlow
{
    public const string UncheckedGlyph = "☐"; // ☐
    public const string CheckedGlyph = "☑";   // ☑
    private const string TaskTag = "taskglyph";
    private static readonly FontFamily SymbolFont = new("Segoe UI Symbol");

    public static readonly (string Name, Color Color)[] Highlights =
    {
        ("Yellow", HexColor("#FFF59D")),
        ("Green",  HexColor("#C8E6C9")),
        ("Blue",   HexColor("#BBDEFB")),
        ("Pink",   HexColor("#F8BBD0")),
    };

    public static readonly Color DefaultHighlight = HexColor("#FFF59D");
    public static readonly Color HighlightForeground = HexColor("#1A1A1A");
    public static readonly Brush HighlightForegroundBrush = Frozen(HighlightForeground);

    private static Color HexColor(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // ---------------------------------------------------------------- parse

    public static FlowDocument ToFlowDocument(string? markdown)
    {
        var doc = new FlowDocument();
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
            doc.Blocks.Add(BuildParagraph(line));
        if (doc.Blocks.Count == 0)
            doc.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
        return doc;
    }

    private static readonly Regex TaskRe =
        new(@"^[ \t]*[-*] \[([ xX])\][ ]?(.*)$", RegexOptions.Compiled);

    private static Paragraph BuildParagraph(string line)
    {
        var p = new Paragraph { Margin = new Thickness(0) };

        var m = TaskRe.Match(line);
        if (m.Success)
        {
            var isChecked = m.Groups[1].Value is "x" or "X";
            p.Inlines.Add(MakeTaskGlyphRun(isChecked));
            foreach (var run in ParseInlines(m.Groups[2].Value))
                p.Inlines.Add(run);
            if (isChecked)
                SetTaskCompletedVisual(p, true);
            return p;
        }

        foreach (var run in ParseInlines(line))
            p.Inlines.Add(run);
        return p;
    }

    /// <summary>The leading clickable task glyph (☐/☑) for a task line.</summary>
    public static Run MakeTaskGlyphRun(bool isChecked) =>
        new((isChecked ? CheckedGlyph : UncheckedGlyph) + " ")
        {
            Tag = TaskTag,
            FontFamily = SymbolFont
        };

    public static bool TryGetTaskGlyph(Paragraph p, out Run glyph, out bool isChecked)
    {
        glyph = null!;
        isChecked = false;
        if (p.Inlines.FirstInline is Run r && (r.Tag as string) == TaskTag)
        {
            glyph = r;
            isChecked = r.Text.StartsWith(CheckedGlyph, StringComparison.Ordinal);
            return true;
        }
        return false;
    }

    /// <summary>Set/clear the checked glyph and strike-through on a task line.</summary>
    public static void SetTaskCompletedVisual(Paragraph p, bool done)
    {
        foreach (var inline in p.Inlines)
        {
            if (inline is not Run r) continue;
            if ((r.Tag as string) == TaskTag)
                r.Text = (done ? CheckedGlyph : UncheckedGlyph) + " ";
            else
                r.TextDecorations = done ? TextDecorations.Strikethrough : null;
        }
    }

    private static List<Run> ParseInlines(string text)
    {
        var runs = new List<Run>();
        ParseInto(runs, text, false, false, null);
        if (runs.Count == 0)
            runs.Add(new Run(string.Empty));
        return runs;
    }

    private static readonly Regex MarkRe = new(
        @"^<mark(?:\s+style\s*=\s*""[^""]*background\s*:\s*(#[0-9A-Fa-f]{3,8})[^""]*"")?\s*>(.*?)</mark>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static void ParseInto(List<Run> runs, string text, bool bold, bool italic, Brush? bg)
    {
        var plain = new StringBuilder();
        void Flush()
        {
            if (plain.Length > 0)
            {
                runs.Add(MakeRun(plain.ToString(), bold, italic, bg));
                plain.Clear();
            }
        }

        int i = 0;
        while (i < text.Length)
        {
            if (TryPair(text, i, "**", out int closeB))
            {
                Flush();
                ParseInto(runs, text.Substring(i + 2, closeB - (i + 2)), true, italic, bg);
                i = closeB + 2;
                continue;
            }
            if (TryPair(text, i, "==", out int closeH))
            {
                Flush();
                ParseInto(runs, text.Substring(i + 2, closeH - (i + 2)), bold, italic, Frozen(DefaultHighlight));
                i = closeH + 2;
                continue;
            }
            if (text[i] == '<')
            {
                var mm = MarkRe.Match(text, i);
                if (mm.Success && mm.Index == i)
                {
                    Flush();
                    var color = DefaultHighlight;
                    if (mm.Groups[1].Success)
                    {
                        try { color = (Color)ColorConverter.ConvertFromString(mm.Groups[1].Value)!; }
                        catch { color = DefaultHighlight; }
                    }
                    ParseInto(runs, mm.Groups[2].Value, bold, italic, Frozen(color));
                    i += mm.Length;
                    continue;
                }
            }
            if (TryItalic(text, i, out int closeI))
            {
                Flush();
                ParseInto(runs, text.Substring(i + 1, closeI - (i + 1)), bold, true, bg);
                i = closeI + 1;
                continue;
            }

            plain.Append(text[i]);
            i++;
        }
        Flush();
    }

    private static Run MakeRun(string text, bool bold, bool italic, Brush? bg)
    {
        var r = new Run(text);
        if (bold) r.FontWeight = FontWeights.Bold;
        if (italic) r.FontStyle = FontStyles.Italic;
        if (bg != null)
        {
            r.Background = bg;
            r.Foreground = HighlightForegroundBrush;
        }
        return r;
    }

    private static bool TryPair(string s, int i, string marker, out int closeStart)
    {
        closeStart = -1;
        if (i + marker.Length > s.Length) return false;
        if (string.CompareOrdinal(s, i, marker, 0, marker.Length) != 0) return false;
        int from = i + marker.Length;
        int idx = s.IndexOf(marker, from, StringComparison.Ordinal);
        if (idx <= from) return false;
        closeStart = idx;
        return true;
    }

    private static bool TryItalic(string s, int i, out int closeStart)
    {
        closeStart = -1;
        if (s[i] != '*') return false;
        if (i + 1 < s.Length && s[i + 1] == '*') return false;
        for (int j = i + 1; j < s.Length; j++)
        {
            if (s[j] == '*' && s[j - 1] != '*' && (j + 1 >= s.Length || s[j + 1] != '*'))
            {
                if (j == i + 1) return false;
                closeStart = j;
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------- serialize

    public static string ToMarkdown(FlowDocument doc) => ToMarkdown(doc, null);

    public static string ToMarkdown(FlowDocument doc, Block? skip)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var block in doc.Blocks)
        {
            if (ReferenceEquals(block, skip)) continue;
            if (!first) sb.Append('\n');
            first = false;
            if (block is Paragraph p)
                sb.Append(SerializeParagraph(p));
        }
        return sb.ToString();
    }

    private static string SerializeParagraph(Paragraph p)
    {
        var sb = new StringBuilder();
        var inlines = p.Inlines.ToList();
        int start = 0;

        if (inlines.Count > 0 && inlines[0] is Run g && (g.Tag as string) == TaskTag)
        {
            sb.Append(g.Text.StartsWith(CheckedGlyph, StringComparison.Ordinal) ? "- [x] " : "- [ ] ");
            start = 1;
        }

        var tokens = new List<(string text, bool bold, bool italic, Brush? bg)>();
        for (int k = start; k < inlines.Count; k++)
        {
            if (inlines[k] is Run run)
                tokens.Add((run.Text, IsBold(run), IsItalic(run), HighlightOf(run)));
        }

        int idx = 0;
        while (idx < tokens.Count)
        {
            var t = tokens[idx];
            var text = t.text;
            int j = idx + 1;
            while (j < tokens.Count &&
                   tokens[j].bold == t.bold &&
                   tokens[j].italic == t.italic &&
                   SameBrush(tokens[j].bg, t.bg))
            {
                text += tokens[j].text;
                j++;
            }
            sb.Append(Wrap(text, t.bold, t.italic, t.bg));
            idx = j;
        }

        return sb.ToString();
    }

    private static string Wrap(string text, bool bold, bool italic, Brush? bg)
    {
        if (text.Length == 0) return string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return text;

        var s = text;
        if (italic) s = $"*{s}*";
        if (bold) s = $"**{s}**";
        if (bg is SolidColorBrush scb)
        {
            if (scb.Color == DefaultHighlight)
                s = $"=={s}==";
            else
                s = $"<mark style=\"background:#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}\">{s}</mark>";
        }
        return s;
    }

    private static bool IsBold(Run r) => r.FontWeight.ToOpenTypeWeight() >= 600;
    private static bool IsItalic(Run r) => r.FontStyle == FontStyles.Italic;

    private static Brush? HighlightOf(Run r)
    {
        if (r.Background is SolidColorBrush scb && scb.Color.A != 0)
            return scb;
        return null;
    }

    private static bool SameBrush(Brush? a, Brush? b)
    {
        if (a is null && b is null) return true;
        if (a is SolidColorBrush x && b is SolidColorBrush y) return x.Color == y.Color;
        return false;
    }
}
