using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Termox.Services;

internal static class LocalPathSafety
{
    internal static string EnsureWithinRoot(string rootPath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path cannot be null or empty.", nameof(rootPath));
        if (string.IsNullOrWhiteSpace(candidatePath))
            throw new ArgumentException("Candidate path cannot be null or empty.", nameof(candidatePath));

        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath);
        var rootPrefix = root + Path.DirectorySeparatorChar;

        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.Equals(root, comparison) &&
            !candidate.StartsWith(rootPrefix, comparison))
            throw new InvalidOperationException("Remote path is outside the selected download folder.");

        return candidate;
    }
}
