using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Termox.Services;

/// <summary>
/// Reference-equality check across two bound values, used to show a checkmark next to
/// whichever row in the server-picker flyout matches the chat's current SelectedServerProfile
/// (the row's own DataContext vs. the ambient VM property — two different binding sources,
/// hence a MultiBinding instead of a single converter).
/// </summary>
public class ProfileMatchConverter : IMultiValueConverter
{
    public static readonly ProfileMatchConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count >= 2 && ReferenceEquals(values[0], values[1]);
}
