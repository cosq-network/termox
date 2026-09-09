using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Termox.Models;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

/// <summary>
/// One chat transcript entry. Implements INotifyPropertyChanged (Content only) so a
/// streaming assistant reply can be appended to in place and have the bound UI update
/// live, instead of replacing the whole message object on every delta.
/// </summary>
public class ChatMessage : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ChatRole Role { get; set; } = ChatRole.User;

    private string _content = "";
    public string Content
    {
        get => _content;
        set
        {
            _content = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWaitingForContent));
            OnPropertyChanged(nameof(IsAssistantWithContent));
        }
    }

    public List<ChatToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// True for a message reporting a failure (endpoint/network/tool error) rather than
    /// genuine model output — rendered with distinct (red/orange) styling instead of the
    /// normal assistant bubble so a "402 Payment Required" doesn't read as the model's reply.
    /// </summary>
    public bool IsError { get; set; }

    public bool IsUser => Role == ChatRole.User && !IsError;
    public bool IsAssistant => Role == ChatRole.Assistant && !IsError;
    public bool IsTool => Role == ChatRole.Tool;

    /// <summary>
    /// True for a freshly-added assistant bubble that has no content yet — shown as a
    /// "thinking" indicator instead of a blank space while waiting for the first token
    /// (or a tool result) to arrive.
    /// </summary>
    public bool IsWaitingForContent => IsAssistant && string.IsNullOrEmpty(Content);

    /// <summary>Assistant bubble that has real content to render — gates the markdown view.</summary>
    public bool IsAssistantWithContent => IsAssistant && !string.IsNullOrEmpty(Content);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
