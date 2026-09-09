using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Termox.Services;

/// <summary>
/// Builds an Avalonia visual tree from MiniMarkdown's parsed blocks. Kept deliberately
/// simple (TextBlock/Grid/Border only, no custom control) so it only depends on Avalonia
/// APIs stable across the 12.x line — the whole point of not pulling in a third-party
/// markdown control was avoiding exactly the kind of version binary-compatibility break
/// that motivated this class in the first place.
/// </summary>
public static class MarkdownRenderer
{
    private const string MonospaceFont = "Consolas, Fira Code, monospace";
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#e0e0e0"));
    private static readonly IBrush CodeBrush = new SolidColorBrush(Color.Parse("#f0b25f"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#888888"));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.Parse("#3a3a3a"));

    public static Control Render(string? markdown)
    {
        var blocks = MiniMarkdown.Parse(markdown);
        var panel = new StackPanel { Spacing = 6 };

        foreach (var block in blocks)
        {
            panel.Children.Add(block switch
            {
                MdHeading heading => RenderHeading(heading),
                MdParagraph paragraph => RenderParagraph(paragraph.Inlines),
                MdBulletList list => RenderList(list.Items, ordered: false),
                MdNumberedList list => RenderList(list.Items, ordered: true),
                MdCodeBlock code => RenderCodeBlock(code.Code),
                MdTable table => RenderTable(table),
                MdHorizontalRule => RenderHorizontalRule(),
                _ => new TextBlock()
            });
        }

        return panel;
    }

    private static TextBlock RenderHeading(MdHeading heading)
    {
        var textBlock = RenderParagraph(heading.Inlines);
        textBlock.FontWeight = FontWeight.Bold;
        textBlock.FontSize = heading.Level switch
        {
            1 => 18,
            2 => 16,
            3 => 15,
            _ => 14
        };
        return textBlock;
    }

    private static TextBlock RenderParagraph(List<MdInline> inlines)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = TextBrush,
            FontSize = 13
        };
        foreach (var run in BuildRuns(inlines))
            textBlock.Inlines!.Add(run);
        return textBlock;
    }

    private static IEnumerable<Run> BuildRuns(List<MdInline> inlines)
    {
        foreach (var inline in inlines)
        {
            yield return new Run(inline.Text)
            {
                FontWeight = inline.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontFamily = inline.Code ? new FontFamily(MonospaceFont) : FontFamily.Default,
                Foreground = inline.Code ? CodeBrush : TextBrush
            };
        }
    }

    private static Control RenderList(List<List<MdInline>> items, bool ordered)
    {
        var stack = new StackPanel { Spacing = 3 };
        for (var i = 0; i < items.Count; i++)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*") };
            row.Children.Add(new TextBlock
            {
                Text = ordered ? $"{i + 1}." : "•",
                Foreground = MutedBrush,
                FontSize = 13
            });
            var content = RenderParagraph(items[i]);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            stack.Children.Add(row);
        }
        return stack;
    }

    private static Control RenderCodeBlock(string code)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#161616")),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = code,
                    FontFamily = new FontFamily(MonospaceFont),
                    FontSize = 12,
                    Foreground = TextBrush
                }
            }
        };
    }

    private static Control RenderTable(MdTable table)
    {
        var columnCount = table.Headers.Count;
        var grid = new Grid();
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 60 });
        for (var r = 0; r <= table.Rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var c = 0; c < columnCount; c++)
        {
            var header = RenderParagraph(table.Headers[c]);
            header.FontWeight = FontWeight.Bold;
            var cell = WrapTableCell(header, isHeader: true);
            Grid.SetRow(cell, 0);
            Grid.SetColumn(cell, c);
            grid.Children.Add(cell);
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < columnCount && c < row.Count; c++)
            {
                var cell = WrapTableCell(RenderParagraph(row[c]), isHeader: false);
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = grid
            }
        };
    }

    private static Border WrapTableCell(Control content, bool isHeader)
    {
        return new Border
        {
            Background = isHeader ? new SolidColorBrush(Color.Parse("#2a2a2a")) : Brushes.Transparent,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(8, 5),
            Child = content
        };
    }

    private static Control RenderHorizontalRule() => new Border
    {
        Height = 1,
        Background = BorderBrush,
        Margin = new Thickness(0, 4)
    };
}
