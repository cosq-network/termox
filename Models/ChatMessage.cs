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
            OnPropertyChanged(nameof(IsPlainAssistantReply));
        }
    }

    private List<ChatToolCall>? _toolCalls;

    /// <summary>
    /// Notifies (unlike a plain auto-property) because this is set AFTER the message is
    /// already in the UI-bound transcript — the model streams a tool-call intent only once
    /// its arguments finish accumulating, well after the placeholder bubble was added.
    /// </summary>
    public List<ChatToolCall>? ToolCalls
    {
        get => _toolCalls;
        set
        {
            _toolCalls = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasToolCalls));
            OnPropertyChanged(nameof(IsPlainAssistantReply));
        }
    }

    public bool HasToolCalls => ToolCalls is { Count: > 0 };

    public string? ToolCallId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    private string _statusText = "Thinking…";

    /// <summary>
    /// What the "waiting" indicator shows while this placeholder has no content yet.
    /// Set once, right before the message is added to the transcript, to something
    /// specific to what's actually happening this round (e.g. "Reviewing ssh_run_command
    /// result…" after a tool call) instead of a generic "thinking…" that looks identical
    /// whether the model just started or is stuck.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _isError;

    /// <summary>
    /// True for a message reporting a failure (endpoint/network/tool error) rather than
    /// genuine model output — rendered with distinct (red/orange) styling instead of the
    /// normal assistant bubble so a "402 Payment Required" doesn't read as the model's reply.
    /// Notifies the same derived properties Content does (IsUser/IsAssistant depend on it
    /// too) — needed for the case where a message starts as a normal assistant placeholder
    /// and only later turns out to be an error (e.g. an empty final reply from the model).
    /// </summary>
    public bool IsError
    {
        get => _isError;
        set
        {
            _isError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUser));
            OnPropertyChanged(nameof(IsAssistant));
            OnPropertyChanged(nameof(IsWaitingForContent));
            OnPropertyChanged(nameof(IsAssistantWithContent));
            OnPropertyChanged(nameof(IsPlainAssistantReply));
        }
    }

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

    /// <summary>
    /// A genuine prose reply — as opposed to the synthesized "● tool(args)" announcement
    /// line a tool-calling turn gets instead. The two need different rendering (markdown
    /// vs. plain monospace) even though both leave IsAssistantWithContent true.
    /// </summary>
    public bool IsPlainAssistantReply => IsAssistantWithContent && !HasToolCalls;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
