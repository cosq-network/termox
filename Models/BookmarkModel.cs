using System;

namespace Termox.Models;

public class BookmarkModel
{
    private string _path = "/";

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
}
