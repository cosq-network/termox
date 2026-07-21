using System;

namespace Termox.Models;

public class RemoteFileModel
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Length { get; set; }
    public DateTime LastWriteTime { get; set; }
    
    public string Permissions { get; set; } = "";
    
    // For UI Display
    public string IconDisplay => IsDirectory ? "📁" : "📄";
    public string SizeDisplay => IsDirectory ? "" : FormatSize(Length);
    public string ModifiedDisplay => LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

    private string FormatSize(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB" };
        if (bytes == 0) return "0 B";
        long bytesAbsolute = Math.Abs(bytes);
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytesAbsolute, 1024)));
        double num = Math.Round(bytesAbsolute / Math.Pow(1024, place), 1);
        return $"{Math.Sign(bytes) * num} {suf[place]}";
    }
}
