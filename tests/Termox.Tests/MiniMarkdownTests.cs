using System.Linq;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class MiniMarkdownTests
{
    [Fact]
    public void ParsesBoldAndInlineCodeSpans()
    {
        var inlines = MiniMarkdown.ParseInlines("run **ls -la** or `pwd` now");

        Assert.Equal("run ", inlines[0].Text);
        Assert.False(inlines[0].Bold);
        Assert.Equal("ls -la", inlines[1].Text);
        Assert.True(inlines[1].Bold);
        Assert.Equal(" or ", inlines[2].Text);
        Assert.Equal("pwd", inlines[3].Text);
        Assert.True(inlines[3].Code);
        Assert.Equal(" now", inlines[4].Text);
    }

    [Fact]
    public void ParsesHeadingLevelFromHashCount()
    {
        var blocks = MiniMarkdown.Parse("### Heading Three");

        var heading = Assert.IsType<MdHeading>(Assert.Single(blocks));
        Assert.Equal(3, heading.Level);
        Assert.Equal("Heading Three", heading.Inlines.Single().Text);
    }

    [Fact]
    public void ParsesConsecutiveBulletItemsIntoOneList()
    {
        var blocks = MiniMarkdown.Parse("- first\n- second\n- third");

        var list = Assert.IsType<MdBulletList>(Assert.Single(blocks));
        Assert.Equal(3, list.Items.Count);
        Assert.Equal("first", list.Items[0].Single().Text);
        Assert.Equal("third", list.Items[2].Single().Text);
    }

    [Fact]
    public void ParsesNumberedList()
    {
        var blocks = MiniMarkdown.Parse("1. one\n2. two");

        var list = Assert.IsType<MdNumberedList>(Assert.Single(blocks));
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void ParsesFencedCodeBlockVerbatim()
    {
        var blocks = MiniMarkdown.Parse("```\nls -la\ndf -h\n```");

        var code = Assert.IsType<MdCodeBlock>(Assert.Single(blocks));
        Assert.Equal("ls -la\ndf -h", code.Code);
    }

    [Fact]
    public void ParsesPipeTableWithHeaderAndRows()
    {
        var markdown = "| Category | What I can do |\n|---|---|\n| SSH | Run commands |\n| SFTP | List files |";

        var blocks = MiniMarkdown.Parse(markdown);

        var table = Assert.IsType<MdTable>(Assert.Single(blocks));
        Assert.Equal(2, table.Headers.Count);
        Assert.Equal("Category", table.Headers[0].Single().Text);
        Assert.Equal("What I can do", table.Headers[1].Single().Text);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("SSH", table.Rows[0][0].Single().Text);
        Assert.Equal("List files", table.Rows[1][1].Single().Text);
    }

    [Fact]
    public void TreatsLiteralBrTagsAsLineBreaksNotLiteralText()
    {
        var blocks = MiniMarkdown.Parse("line one<br>line two");

        // <br> splits into two paragraphs (blank-line-free break), and the literal
        // tag text itself must not survive into the rendered inlines.
        var allText = string.Join("", blocks.OfType<MdParagraph>().SelectMany(p => p.Inlines).Select(i => i.Text));
        Assert.DoesNotContain("<br>", allText);
    }

    [Fact]
    public void EmptyOrNullMarkdownProducesNoBlocks()
    {
        Assert.Empty(MiniMarkdown.Parse(""));
        Assert.Empty(MiniMarkdown.Parse(null));
    }

    [Fact]
    public void ParsesHorizontalRule()
    {
        var blocks = MiniMarkdown.Parse("above\n\n---\n\nbelow");

        Assert.Contains(blocks, b => b is MdHorizontalRule);
    }

    [Fact]
    public void PlainParagraphHasNoSpecialFormatting()
    {
        var blocks = MiniMarkdown.Parse("just a normal sentence.");

        var paragraph = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        Assert.False(paragraph.Inlines.Single().Bold);
        Assert.False(paragraph.Inlines.Single().Code);
    }
}
