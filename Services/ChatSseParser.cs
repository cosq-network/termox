using System;

namespace Termox.Services;

/// <summary>
/// Parses Server-Sent Events lines from an OpenAI-compatible streaming
/// chat-completions response ("data: {...}" until "data: [DONE]").
/// </summary>
public static class ChatSseParser
{
    private const string DataPrefix = "data:";
    private const string DoneSentinel = "[DONE]";

    /// <summary>
    /// Attempts to parse one raw SSE line. Returns false for blank lines, comment
    /// lines (starting with ':'), non-"data:" fields, or the "[DONE]" sentinel —
    /// callers should treat a false result with a null payload as "nothing to do"
    /// rather than an error, except when <paramref name="isDone"/> is true.
    /// </summary>
    public static bool TryParseSseLine(string? line, out string? jsonPayload, out bool isDone)
    {
        jsonPayload = null;
        isDone = false;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();
        if (trimmed.StartsWith(':'))
            return false; // SSE comment/heartbeat line.

        if (!trimmed.StartsWith(DataPrefix, StringComparison.Ordinal))
            return false;

        var payload = trimmed[DataPrefix.Length..].Trim();
        if (payload.Length == 0)
            return false;

        if (payload == DoneSentinel)
        {
            isDone = true;
            return true;
        }

        jsonPayload = payload;
        return true;
    }
}
