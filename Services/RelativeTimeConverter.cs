using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Termox.Services;

/// <summary>
/// Formats a DateTime as a short relative label ("5m", "2h", "3d") for the chat History
/// panel's session list, instead of an absolute timestamp — matches the compact style of
/// most chat-history UIs.
/// </summary>
public class RelativeTimeConverter : IValueConverter
{
    public static readonly RelativeTimeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dt) return "";
        return Format(dt, DateTime.Now);
    }

    public static string Format(DateTime timestamp, DateTime now)
    {
        var local = timestamp.Kind == DateTimeKind.Utc ? timestamp.ToLocalTime() : timestamp;
        var delta = now - local;

        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta.TotalMinutes < 1) return "now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h";
        if (delta.TotalDays < 30) return $"{(int)delta.TotalDays}d";
        if (delta.TotalDays < 365) return $"{(int)(delta.TotalDays / 30)}mo";
        return $"{(int)(delta.TotalDays / 365)}y";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
