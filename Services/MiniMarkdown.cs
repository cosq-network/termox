using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Termox.Services;

/// <summary>One inline text span: plain, bold, or inline-code.</summary>
public class MdInline
{
    public required string Text { get; init; }
    public bool Bold { get; init; }
    public bool Code { get; init; }
}

public abstract class MdBlock { }

public class MdHeading : MdBlock
{
    public required int Level { get; init; }
    public required List<MdInline> Inlines { get; init; }
}

public class MdParagraph : MdBlock
{
    public required List<MdInline> Inlines { get; init; }
}

public class MdBulletList : MdBlock
{
    public required List<List<MdInline>> Items { get; init; }
}

public class MdNumberedList : MdBlock
{
    public required List<List<MdInline>> Items { get; init; }
}

public class MdCodeBlock : MdBlock
{
    public required string Code { get; init; }
}

public class MdTable : MdBlock
{
    public required List<List<MdInline>> Headers { get; init; }
    public required List<List<List<MdInline>>> Rows { get; init; }
}

public class MdHorizontalRule : MdBlock { }

/// <summary>
/// A small, deliberately non-exhaustive Markdown parser covering what LLM chat replies
/// actually use in practice — headings, paragraphs, bold/inline-code spans, bullet/numbered
/// lists, fenced code blocks, pipe tables, and horizontal rules — rendered with Termox's own
/// Avalonia visual builder (MarkdownRenderer) instead of a third-party control library, to
/// avoid the Avalonia-version binary-compatibility risk a prebuilt library would carry.
/// Pure and side-effect-free so it's unit-testable without any UI dependency.
/// </summary>
public static class MiniMarkdown
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^\s*[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex NumberedRegex = new(@"^\s*\d+\.\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex HorizontalRuleRegex = new(@"^\s*([-*_])\1{2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex TableRowRegex = new(@"^\s*\|?(.+)\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex TableSeparatorRegex = new(@"^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex InlineSpanRegex = new(@"(\*\*.+?\*\*|`.+?`)", RegexOptions.Compiled);

    public static List<MdBlock> Parse(string? markdown)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrEmpty(markdown))
            return blocks;

        // Treat literal HTML line breaks the same as real newlines — chat models
        // sometimes emit "<br>" inside table cells or paragraphs.
        var normalized = markdown.Replace("<br/>", "\n").Replace("<br>", "\n").Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var paragraphBuffer = new List<string>();

        void FlushParagraph()
        {
            if (paragraphBuffer.Count == 0) return;
            blocks.Add(new MdParagraph { Inlines = ParseInlines(string.Join(" ", paragraphBuffer)) });
            paragraphBuffer.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            if (line.TrimStart().StartsWith("```"))
            {
                FlushParagraph();
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                {
                    code.Add(lines[i]);
                    i++;
                }
                blocks.Add(new MdCodeBlock { Code = string.Join("\n", code) });
                continue;
            }

            var headingMatch = HeadingRegex.Match(line);
            if (headingMatch.Success)
            {
                FlushParagraph();
                blocks.Add(new MdHeading
                {
                    Level = headingMatch.Groups[1].Value.Length,
                    Inlines = ParseInlines(headingMatch.Groups[2].Value.Trim())
                });
                continue;
            }

            if (HorizontalRuleRegex.IsMatch(line))
            {
                FlushParagraph();
                blocks.Add(new MdHorizontalRule());
                continue;
            }

            // Pipe table: a "| a | b |"-style row followed by a "|---|---|" separator.
            if (line.Contains('|') && i + 1 < lines.Length && TableSeparatorRegex.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
            {
                FlushParagraph();
                var headers = SplitTableRow(line);
                i += 2; // skip header + separator
                var rows = new List<List<List<MdInline>>>();
                while (i < lines.Length && lines[i].Contains('|') && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    rows.Add(SplitTableRow(lines[i]).ConvertAll(ParseInlines));
                    i++;
                }
                i--; // outer loop will increment
                blocks.Add(new MdTable { Headers = headers.ConvertAll(ParseInlines), Rows = rows });
                continue;
            }

            var bulletMatch = BulletRegex.Match(line);
            if (bulletMatch.Success)
            {
                FlushParagraph();
                var items = new List<List<MdInline>> { ParseInlines(bulletMatch.Groups[1].Value) };
                while (i + 1 < lines.Length && BulletRegex.IsMatch(lines[i + 1]))
                {
                    i++;
                    items.Add(ParseInlines(BulletRegex.Match(lines[i]).Groups[1].Value));
                }
                blocks.Add(new MdBulletList { Items = items });
                continue;
            }

            var numberedMatch = NumberedRegex.Match(line);
            if (numberedMatch.Success)
            {
                FlushParagraph();
                var items = new List<List<MdInline>> { ParseInlines(numberedMatch.Groups[1].Value) };
                while (i + 1 < lines.Length && NumberedRegex.IsMatch(lines[i + 1]))
                {
                    i++;
                    items.Add(ParseInlines(NumberedRegex.Match(lines[i]).Groups[1].Value));
                }
                blocks.Add(new MdNumberedList { Items = items });
                continue;
            }

            paragraphBuffer.Add(line.Trim());
        }

        FlushParagraph();
        return blocks;
    }

    private static List<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        var cells = new List<string>();
        foreach (var cell in trimmed.Split('|'))
            cells.Add(cell.Trim());
        return cells;
    }

    /// <summary>Splits a line of text into plain/bold/code spans on **bold** and `code` markers.</summary>
    public static List<MdInline> ParseInlines(string text)
    {
        var result = new List<MdInline>();
        var lastIndex = 0;

        foreach (Match match in InlineSpanRegex.Matches(text))
        {
            if (match.Index > lastIndex)
                result.Add(new MdInline { Text = text[lastIndex..match.Index] });

            var token = match.Value;
            if (token.StartsWith("**"))
                result.Add(new MdInline { Text = token[2..^2], Bold = true });
            else
                result.Add(new MdInline { Text = token[1..^1], Code = true });

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            result.Add(new MdInline { Text = text[lastIndex..] });

        return result;
    }
}
