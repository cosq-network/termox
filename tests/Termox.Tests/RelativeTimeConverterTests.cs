using System;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class RelativeTimeConverterTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Local);

    [Theory]
    [InlineData(0, "now")]
    [InlineData(30, "now")]
    [InlineData(60, "1m")]
    [InlineData(5 * 60, "5m")]
    [InlineData(59 * 60, "59m")]
    public void FormatsSecondsAndMinutes(int secondsAgo, string expected)
    {
        var result = RelativeTimeConverter.Format(Now.AddSeconds(-secondsAgo), Now);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1, "1h")]
    [InlineData(23, "23h")]
    public void FormatsHours(int hoursAgo, string expected)
    {
        var result = RelativeTimeConverter.Format(Now.AddHours(-hoursAgo), Now);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1, "1d")]
    [InlineData(29, "29d")]
    public void FormatsDays(int daysAgo, string expected)
    {
        var result = RelativeTimeConverter.Format(Now.AddDays(-daysAgo), Now);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatsMonths()
    {
        var result = RelativeTimeConverter.Format(Now.AddDays(-60), Now);
        Assert.Equal("2mo", result);
    }

    [Fact]
    public void FormatsYears()
    {
        var result = RelativeTimeConverter.Format(Now.AddDays(-400), Now);
        Assert.Equal("1y", result);
    }

    [Fact]
    public void FutureTimestampClampsToNow()
    {
        var result = RelativeTimeConverter.Format(Now.AddMinutes(5), Now);
        Assert.Equal("now", result);
    }
}
