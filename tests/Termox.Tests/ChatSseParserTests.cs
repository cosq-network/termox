using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ChatSseParserTests
{
    [Fact]
    public void ParsesValidDataLine()
    {
        var parsed = ChatSseParser.TryParseSseLine("data: {\"foo\":1}", out var payload, out var isDone);

        Assert.True(parsed);
        Assert.False(isDone);
        Assert.Equal("{\"foo\":1}", payload);
    }

    [Fact]
    public void RecognizesDoneSentinel()
    {
        var parsed = ChatSseParser.TryParseSseLine("data: [DONE]", out var payload, out var isDone);

        Assert.True(parsed);
        Assert.True(isDone);
        Assert.Null(payload);
    }

    [Fact]
    public void IgnoresBlankLines()
    {
        Assert.False(ChatSseParser.TryParseSseLine("", out _, out _));
        Assert.False(ChatSseParser.TryParseSseLine("   ", out _, out _));
        Assert.False(ChatSseParser.TryParseSseLine(null, out _, out _));
    }

    [Fact]
    public void IgnoresCommentLines()
    {
        Assert.False(ChatSseParser.TryParseSseLine(": keep-alive", out _, out _));
    }

    [Fact]
    public void IgnoresNonDataFields()
    {
        Assert.False(ChatSseParser.TryParseSseLine("event: ping", out _, out _));
    }
}
