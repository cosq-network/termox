using System.Collections.Generic;
using System.Linq;
using System.Text;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Accumulates fragmented streaming tool-call deltas (OpenAI's Chat Completions
/// streaming protocol sends each tool call's id/name once, on its first chunk, then
/// streams "arguments" as a sequence of string fragments keyed by array index) into
/// finished <see cref="ChatToolCall"/> records once the stream's finish_reason arrives.
/// </summary>
public class ChatToolCallAccumulator
{
    private class Entry
    {
        public string? Id;
        public string? FunctionName;
        public readonly StringBuilder Arguments = new();
    }

    private readonly Dictionary<int, Entry> _byIndex = new();
    private readonly List<int> _order = new();

    public void Apply(int index, string? id, string? functionName, string? argumentsFragment)
    {
        if (!_byIndex.TryGetValue(index, out var entry))
        {
            entry = new Entry();
            _byIndex[index] = entry;
            _order.Add(index);
        }

        if (!string.IsNullOrEmpty(id)) entry.Id = id;
        if (!string.IsNullOrEmpty(functionName)) entry.FunctionName = functionName;
        if (!string.IsNullOrEmpty(argumentsFragment)) entry.Arguments.Append(argumentsFragment);
    }

    public bool HasAny => _order.Count > 0;

    public List<ChatToolCall> Build()
    {
        return _order
            .Select(index => _byIndex[index])
            .Select(entry => new ChatToolCall
            {
                Id = entry.Id ?? "",
                FunctionName = entry.FunctionName ?? "",
                ArgumentsJson = entry.Arguments.ToString()
            })
            .ToList();
    }

    public void Reset()
    {
        _byIndex.Clear();
        _order.Clear();
    }
}
