using System;

namespace Termox.Services;

/// <summary>
/// Rough token-count heuristic used to keep outgoing chat history within a model's
/// context window. Termox has no tokenizer dependency for any provider, so this
/// deliberately approximates (~4 characters per token for English text) rather than
/// counting exactly — good enough for a trim budget, not for billing accuracy.
/// </summary>
public static class TokenEstimator
{
    private const double CharsPerToken = 4.0;

    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Math.Max(1, (int)Math.Ceiling(text.Length / CharsPerToken));
    }
}
