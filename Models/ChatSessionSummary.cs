using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Termox.Models;

/// <summary>Lightweight row for the session-history list — no message bodies.</summary>
public class ChatSessionSummary : INotifyPropertyChanged
{
    public required string Id { get; init; }

    private string _title = "";
    public required string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }

    // UI-only: true while this row's title is being edited inline in the History panel.
    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
