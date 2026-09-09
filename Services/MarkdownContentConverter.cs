using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Termox.Services;

/// <summary>Binds a raw markdown string to a rendered Avalonia control via MarkdownRenderer.</summary>
public class MarkdownContentConverter : IValueConverter
{
    public static readonly MarkdownContentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        MarkdownRenderer.Render(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
