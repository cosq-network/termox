using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Termox.Models;

namespace Termox.Services;

public abstract class ChatStreamEvent
{
    public class ContentDelta : ChatStreamEvent
    {
        public required string Text { get; init; }
    }

    public class ToolCallDelta : ChatStreamEvent
    {
        public required int Index { get; init; }
        public string? Id { get; init; }
        public string? FunctionName { get; init; }
        public string? ArgumentsFragment { get; init; }
    }

    public class FinishReason : ChatStreamEvent
    {
        public required string Reason { get; init; }
    }
}

/// <summary>
/// Minimal client for an OpenAI-compatible /chat/completions streaming endpoint
/// (works against OpenAI, Azure OpenAI, Ollama, LM Studio, OpenRouter, etc.).
/// The only outbound HTTP code in Termox — no retry policy is applied; a hung
/// request is bounded by RequestTimeout and cancellable via the caller's token.
/// </summary>
public class OpenAiChatClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);
    private readonly HttpClient _httpClient = new();

    public async IAsyncEnumerable<ChatStreamEvent> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        ChatSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var baseUrl = settings.BaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        request.Content = JsonContent.Create(BuildRequestBody(messages, tools, settings));

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Chat endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 2000)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (true)
        {
            linkedCts.Token.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(linkedCts.Token).ConfigureAwait(false);
            if (line == null)
                yield break;

            if (!ChatSseParser.TryParseSseLine(line, out var jsonPayload, out var isDone))
                continue;
            if (isDone)
                yield break;
            if (jsonPayload == null)
                continue;

            foreach (var evt in ParseChunk(jsonPayload))
                yield return evt;
        }
    }

    private static IEnumerable<ChatStreamEvent> ParseChunk(string jsonPayload)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(jsonPayload);
        }
        catch (JsonException)
        {
            yield break; // Skip malformed chunks rather than aborting the whole stream.
        }

        var choice = root?["choices"]?.AsArray().Count > 0 ? root["choices"]![0] : null;
        if (choice == null) yield break;

        var delta = choice["delta"];
        var content = delta?["content"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(content))
            yield return new ChatStreamEvent.ContentDelta { Text = content };

        var toolCalls = delta?["tool_calls"]?.AsArray();
        if (toolCalls != null)
        {
            foreach (var tc in toolCalls)
            {
                if (tc == null) continue;
                var index = tc["index"]?.GetValue<int>() ?? 0;
                var id = tc["id"]?.GetValue<string>();
                var functionName = tc["function"]?["name"]?.GetValue<string>();
                var argumentsFragment = tc["function"]?["arguments"]?.GetValue<string>();
                yield return new ChatStreamEvent.ToolCallDelta
                {
                    Index = index,
                    Id = id,
                    FunctionName = functionName,
                    ArgumentsFragment = argumentsFragment
                };
            }
        }

        var finishReason = choice["finish_reason"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(finishReason))
            yield return new ChatStreamEvent.FinishReason { Reason = finishReason };
    }

    private static object BuildRequestBody(
        IReadOnlyList<ChatMessage> messages, IReadOnlyList<ChatToolDefinition> tools, ChatSettings settings)
    {
        var messagePayload = new JsonArray();
        foreach (var message in messages)
            messagePayload.Add(ToMessageNode(message));

        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = messagePayload,
            ["temperature"] = settings.Temperature,
            ["stream"] = true
        };

        if (tools.Count > 0)
        {
            var toolsArray = new JsonArray();
            foreach (var tool in tools)
            {
                toolsArray.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.ParametersSchema)
                    }
                });
            }
            body["tools"] = toolsArray;
        }

        return body;
    }

    private static JsonObject ToMessageNode(ChatMessage message)
    {
        var node = new JsonObject
        {
            ["role"] = message.Role switch
            {
                ChatRole.System => "system",
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.Tool => "tool",
                _ => "user"
            },
            ["content"] = message.Content
        };

        if (message.ToolCallId != null)
            node["tool_call_id"] = message.ToolCallId;

        if (message.ToolCalls is { Count: > 0 })
        {
            var toolCallsArray = new JsonArray();
            foreach (var call in message.ToolCalls)
            {
                toolCallsArray.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.FunctionName,
                        ["arguments"] = call.ArgumentsJson
                    }
                });
            }
            node["tool_calls"] = toolCallsArray;
        }

        return node;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
