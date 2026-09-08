using System.Collections.Generic;
using System.Linq;

namespace Termox.Services;

/// <summary>
/// One entry in the settings drawer's model dropdown. Termox's chat client only speaks
/// the OpenAI-compatible /chat/completions shape, so any model string technically
/// "works" against a matching endpoint — this list is a curated set of models known to
/// support streaming tool-calling in that shape, to steer users toward a working setup
/// without blocking anyone whose model isn't listed (see CustomModelSentinel).
/// </summary>
public class ChatModelInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Provider { get; init; }
    public int ContextWindowTokens { get; init; }
}

public static class ChatModelCatalog
{
    /// <summary>Sentinel id for the "Custom…" dropdown entry that reveals a free-text model field.</summary>
    public const string CustomModelSentinel = "__custom__";

    public static readonly IReadOnlyList<ChatModelInfo> KnownModels = new List<ChatModelInfo>
    {
        new() { Id = "gpt-4o", DisplayName = "GPT-4o", Provider = "OpenAI", ContextWindowTokens = 128_000 },
        new() { Id = "gpt-4o-mini", DisplayName = "GPT-4o mini", Provider = "OpenAI", ContextWindowTokens = 128_000 },
        new() { Id = "gpt-4-turbo", DisplayName = "GPT-4 Turbo", Provider = "OpenAI", ContextWindowTokens = 128_000 },
        new() { Id = "o4-mini", DisplayName = "o4-mini", Provider = "OpenAI", ContextWindowTokens = 200_000 },
        new() { Id = "anthropic/claude-sonnet-4.5", DisplayName = "Claude Sonnet 4.5", Provider = "OpenRouter", ContextWindowTokens = 200_000 },
        new() { Id = "anthropic/claude-3.5-sonnet", DisplayName = "Claude 3.5 Sonnet", Provider = "OpenRouter", ContextWindowTokens = 200_000 },
        new() { Id = "openai/gpt-4o-mini", DisplayName = "GPT-4o mini", Provider = "OpenRouter", ContextWindowTokens = 128_000 },
        new() { Id = "google/gemini-2.0-flash-001", DisplayName = "Gemini 2.0 Flash", Provider = "OpenRouter", ContextWindowTokens = 1_000_000 },
        new() { Id = "meta-llama/llama-3.3-70b-instruct", DisplayName = "Llama 3.3 70B", Provider = "OpenRouter", ContextWindowTokens = 128_000 },
        new() { Id = "llama3.1", DisplayName = "Llama 3.1 (Ollama)", Provider = "Ollama", ContextWindowTokens = 128_000 },
        new() { Id = "qwen2.5", DisplayName = "Qwen 2.5 (Ollama)", Provider = "Ollama", ContextWindowTokens = 32_000 },
    };

    public static ChatModelInfo? Find(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;
        return KnownModels.FirstOrDefault(m => m.Id == modelId);
    }

    public static bool IsKnownModel(string? modelId) => Find(modelId) != null;
}
