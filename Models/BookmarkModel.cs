using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Termox.Models;

public class BookmarkModel : INotifyPropertyChanged
{
    private string _path = "/";
    private bool _isFavorite;

    public string Path
    {
        get => _path;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Bookmark path cannot be null or empty.", nameof(value));
            if (!value.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("Bookmark path must be an absolute path starting with '/'.", nameof(value));
            _path = value;
        }
    }

    public string ProfileId { get; set; } = "";
    public string SessionName { get; set; } = "Unassigned session";

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteColor));
            OnPropertyChanged(nameof(FavoriteToolTip));
        }
    }

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string FavoriteColor => IsFavorite ? "#f39c12" : "#888";
    public string FavoriteToolTip => IsFavorite ? "Unpin this bookmark" : "Pin this bookmark";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
