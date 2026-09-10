using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Termox.Services;

/// <summary>
/// Bool -> status-dot brush for the chat header's "which server is this scoped to"
/// indicator: green when the selected profile's own IsConnected flag is set, muted
/// grey otherwise (including when no server is selected at all).
/// </summary>
public class ConnectedDotConverter : IValueConverter
{
    public static readonly ConnectedDotConverter Instance = new();

    private static readonly IBrush Connected = new SolidColorBrush(Color.Parse("#4caf50"));
    private static readonly IBrush Disconnected = new SolidColorBrush(Color.Parse("#555555"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Connected : Disconnected;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
