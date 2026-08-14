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

        // A lexical check is insufficient when an existing directory in the
        // destination tree is a symlink/junction. Resolve every existing
        // component so a remote filename cannot redirect writes elsewhere.
        var relative = Path.GetRelativePath(root, candidate);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
                continue;

            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            if (resolved == null)
                continue;

            var resolvedPath = Path.GetFullPath(resolved.FullName)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!resolvedPath.Equals(root, comparison) &&
                !resolvedPath.StartsWith(rootPrefix, comparison))
                throw new InvalidOperationException("Download path traverses outside the selected folder through a link.");
        }

        return candidate;
    }
}
