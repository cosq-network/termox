using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Termox.Models;

public class RemoteFileModel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool IsSymbolicLink { get; set; }
    public long Length { get; set; }
    public DateTime LastWriteTime { get; set; }

    public string Permissions { get; set; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // For UI Display
    public string IconDisplay => IsDirectory ? "📁" : "📄";
    public string SizeDisplay => IsDirectory ? "" : FormatSize(Length);
    public string ModifiedDisplay => LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

    private string FormatSize(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        if (bytes == 0) return "0 B";
        long bytesAbsolute = bytes == long.MinValue ? long.MaxValue : Math.Abs(bytes);
        int place = Math.Min(Convert.ToInt32(Math.Floor(Math.Log(bytesAbsolute, 1024))), suf.Length - 1);
        double num = Math.Round(bytesAbsolute / Math.Pow(1024, place), 1);
        return $"{Math.Sign(bytes) * num} {suf[place]}";
    }
}
