using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Termox.Services;

/// <summary>
/// Collapses a Grid column to zero width when a bool is false, instead of the column's
/// "*"/"Auto" share sitting reserved (and squeezing everything else) just because its
/// content is IsVisible="False" — a plain content IsVisible binding never shrinks the
/// column that hosts it. Pass the desired width for the "true" case as ConverterParameter
/// (e.g. "6" for a splitter's pixel width, "*" for a star-sized pane).
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public static readonly BoolToGridLengthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true) return new GridLength(0);
        var text = parameter as string ?? "*";
        return GridLength.Parse(text);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
