using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Termox.Models;
using Termox.Services;

namespace Termox.ViewModels;

public class ChatTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private readonly ChatSettingsService _settingsService;
    private readonly ChatHistoryService _historyService;
    private readonly ChatToolRegistry _toolRegistry;
    private readonly OpenAiChatClient _client = new();
    private readonly Action<ChatTabViewModel> _onClose;

    public ObservableCollection<SshConnectionProfile> SavedConnections { get; }

    private CancellationTokenSource? _streamCts;
    private TaskCompletionSource<bool>? _pendingApprovalTcs;
    private string? _currentSessionId;
    private int _nextSequence;

    private string _title = "Chat";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    public ICommand DisconnectCommand { get; }
    public ICommand CloseTabCommand { get; }

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    private string _draftInput = "";
    public string DraftInput
    {
        get => _draftInput;
        set { _draftInput = value; OnPropertyChanged(); _sendMessageCommand?.RaiseCanExecuteChanged(); }
    }

    private bool _isStreaming;
    public bool IsStreaming
    {
        get => _isStreaming;
        set { _isStreaming = value; OnPropertyChanged(); _sendMessageCommand?.RaiseCanExecuteChanged(); }
    }

    private bool _isSettingsPanelOpen;
    public bool IsSettingsPanelOpen { get => _isSettingsPanelOpen; set { _isSettingsPanelOpen = value; OnPropertyChanged(); } }

    private bool _isHistoryPanelOpen;
    public bool IsHistoryPanelOpen { get => _isHistoryPanelOpen; set { _isHistoryPanelOpen = value; OnPropertyChanged(); } }

    public ObservableCollection<ChatSessionSummary> HistorySessions { get; } = new();

    // ChatSettings is a plain POCO (no INotifyPropertyChanged) so it must never be bound
    // to directly from XAML — each field below is its own flat, individually-notifying
    // property that reads/writes through to it. A previous version bound XAML controls
    // straight to "Settings.X" and forced updates by raising OnPropertyChanged(nameof(Settings))
    // on every edit; that re-subscribed every Settings.* binding at once and, combined with
    // a TwoWay-bound ComboBox/NumericUpDown, produced an infinite bind/re-publish loop that
    // stack-overflowed the app. Keep these flat and this stays impossible.
    private readonly ChatSettings _settings;

    public string BaseUrl
    {
        get => _settings.BaseUrl;
        set { if (_settings.BaseUrl == value) return; _settings.BaseUrl = value; OnPropertyChanged(); }
    }

    public string ApiKey
    {
        get => _settings.ApiKey;
        set { if (_settings.ApiKey == value) return; _settings.ApiKey = value; OnPropertyChanged(); }
    }

    public double Temperature
    {
        get => _settings.Temperature;
        set { if (_settings.Temperature == value) return; _settings.Temperature = value; OnPropertyChanged(); }
    }

    public int ContextWindowTokens
    {
        get => _settings.ContextWindowTokens;
        set { if (_settings.ContextWindowTokens == value) return; _settings.ContextWindowTokens = value; OnPropertyChanged(); }
    }

    public bool AutoApproveReadOnlyTools
    {
        get => _settings.AutoApproveReadOnlyTools;
        set { if (_settings.AutoApproveReadOnlyTools == value) return; _settings.AutoApproveReadOnlyTools = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<ChatModelInfo> ModelOptions { get; } = ChatModelCatalog.KnownModels
        .Append(new ChatModelInfo { Id = ChatModelCatalog.CustomModelSentinel, DisplayName = "Custom…", Provider = "" })
        .ToList();

    private ChatModelInfo? _selectedModel;
    public ChatModelInfo? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (ReferenceEquals(_selectedModel, value)) return;
            _selectedModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomModelSelected));
            if (value != null && value.Id != ChatModelCatalog.CustomModelSentinel)
            {
                _settings.Model = value.Id;
                ContextWindowTokens = value.ContextWindowTokens;
            }
            OnPropertyChanged(nameof(IsModelUnrecognized));
        }
    }

    public bool IsCustomModelSelected => SelectedModel?.Id == ChatModelCatalog.CustomModelSentinel;

    private string _customModelId = "";
    public string CustomModelId
    {
        get => _customModelId;
        set
        {
            if (_customModelId == value) return;
            _customModelId = value;
            OnPropertyChanged();
            if (IsCustomModelSelected)
                _settings.Model = value;
            OnPropertyChanged(nameof(IsModelUnrecognized));
        }
    }

    /// <summary>
    /// True when the resolved model string isn't in the curated catalog — shown as a
    /// non-blocking warning; Termox's OpenAI-compatible client will still try it as-is.
    /// </summary>
    public bool IsModelUnrecognized => !string.IsNullOrWhiteSpace(_settings.Model) && !ChatModelCatalog.IsKnownModel(_settings.Model);

    // When set, every SSH/SFTP tool call in this chat is locked to this one saved
    // connection (see ChatToolRegistry.ScopedProfileId) — the model is never given a
    // choice of server, and the chat header shows which one it's talking to.
    private SshConnectionProfile? _selectedServerProfile;
    public SshConnectionProfile? SelectedServerProfile
    {
        get => _selectedServerProfile;
        set
        {
            if (ReferenceEquals(_selectedServerProfile, value)) return;
            _selectedServerProfile = value;
            _toolRegistry.ScopedProfileId = value?.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsServerScoped));
        }
    }

    public bool IsServerScoped => SelectedServerProfile != null;

    // Inline picker panel state — deliberately NOT an Avalonia Flyout/Popup: those anchor
    // and size relative to their trigger control, which made it fight to sit flush and
    // full-width against the composer. A plain sibling panel toggled by this bool sits in
    // the same layout as the input box, so it's guaranteed to match its width with no
    // positioning math at all.
    private bool _isServerPickerOpen;
    public bool IsServerPickerOpen { get => _isServerPickerOpen; set { _isServerPickerOpen = value; OnPropertyChanged(); } }

    private ChatToolCall? _pendingApprovalCall;
    public ChatToolCall? PendingApprovalCall { get => _pendingApprovalCall; set { _pendingApprovalCall = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPendingApproval)); } }

    public bool HasPendingApproval => PendingApprovalCall != null;

    private string _pendingApprovalDescription = "";
    public string PendingApprovalDescription { get => _pendingApprovalDescription; set { _pendingApprovalDescription = value; OnPropertyChanged(); } }

    // Concrete RelayCommand (not just ICommand) so DraftInput/IsStreaming's setters can call
    // RaiseCanExecuteChanged() directly — Avalonia has no WPF-style automatic CanExecute
    // requery, so without this the Send button silently never re-enables as you type.
    private RelayCommand? _sendMessageCommand;
    public ICommand SendMessageCommand => _sendMessageCommand!;
    public ICommand CancelStreamingCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ClearTranscriptCommand { get; }
    public ICommand ApproveToolCallCommand { get; }
    public ICommand DenyToolCallCommand { get; }
    public ICommand OpenHistoryCommand { get; }
    public ICommand CloseHistoryCommand { get; }
    public ICommand NewChatCommand { get; }
    public ICommand LoadSessionCommand { get; }
    public ICommand DeleteSessionCommand { get; }
    public ICommand ClearServerScopeCommand { get; }
    public ICommand SelectServerCommand { get; }
    public ICommand ToggleServerPickerCommand { get; }

    public ChatTabViewModel(
        Action<ChatTabViewModel> onClose,
        ObservableCollection<SshConnectionProfile> savedConnections,
        ChatSettingsService settingsService,
        ChatHistoryService historyService)
    {
        _onClose = onClose;
        _settingsService = settingsService;
        _historyService = historyService;
        _settings = _settingsService.Load();
        SavedConnections = savedConnections;
        _toolRegistry = new ChatToolRegistry(savedConnections);

        var knownModel = ChatModelCatalog.Find(_settings.Model);
        if (knownModel != null)
        {
            _selectedModel = knownModel;
        }
        else
        {
            _selectedModel = ModelOptions.First(m => m.Id == ChatModelCatalog.CustomModelSentinel);
            _customModelId = _settings.Model;
        }

        DisconnectCommand = new RelayCommand(() => { });
        CloseTabCommand = new RelayCommand(() =>
        {
            _streamCts?.Cancel();
            _onClose(this);
        });

        _sendMessageCommand = new RelayCommand(() => _ = SendMessageAsync(), () => !IsStreaming && !string.IsNullOrWhiteSpace(DraftInput));
        CancelStreamingCommand = new RelayCommand(() => _streamCts?.Cancel());
        OpenSettingsCommand = new RelayCommand(() => { IsSettingsPanelOpen = true; IsHistoryPanelOpen = false; });
        CloseSettingsCommand = new RelayCommand(() => IsSettingsPanelOpen = false);
        SaveSettingsCommand = new RelayCommand(() =>
        {
            _settingsService.Save(_settings);
            IsSettingsPanelOpen = false;
        });
        ClearTranscriptCommand = new RelayCommand(() => Messages.Clear());
        ApproveToolCallCommand = new RelayCommand(() => ResolveApproval(true));
        DenyToolCallCommand = new RelayCommand(() => ResolveApproval(false));
        OpenHistoryCommand = new RelayCommand(() =>
        {
            RefreshHistoryList();
            IsHistoryPanelOpen = true;
            IsSettingsPanelOpen = false;
        });
        CloseHistoryCommand = new RelayCommand(() => IsHistoryPanelOpen = false);
        NewChatCommand = new RelayCommand(StartNewChat);
        LoadSessionCommand = new RelayCommand<ChatSessionSummary>(LoadSession);
        DeleteSessionCommand = new RelayCommand<ChatSessionSummary>(DeleteSession);
        ClearServerScopeCommand = new RelayCommand(() => { SelectedServerProfile = null; IsServerPickerOpen = false; });
        SelectServerCommand = new RelayCommand<SshConnectionProfile>(p => { SelectedServerProfile = p; IsServerPickerOpen = false; });
        ToggleServerPickerCommand = new RelayCommand(() => IsServerPickerOpen = !IsServerPickerOpen);
    }

    private void RefreshHistoryList()
    {
        List<ChatSessionSummary> sessions;
        try
        {
            sessions = _historyService.ListSessions();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load chat history list: {ex.Message}");
            return;
        }

        HistorySessions.Clear();
        foreach (var session in sessions)
            HistorySessions.Add(session);
    }

    private void StartNewChat()
    {
        _streamCts?.Cancel();
        Messages.Clear();
        _currentSessionId = null;
        _nextSequence = 0;
        IsHistoryPanelOpen = false;
    }

    private void LoadSession(ChatSessionSummary? summary)
    {
        if (summary == null) return;

        List<ChatMessage> loaded;
        try
        {
            loaded = _historyService.LoadMessages(summary.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load chat session '{summary.Id}': {ex.Message}");
            return;
        }

        _streamCts?.Cancel();
        Messages.Clear();
        foreach (var message in loaded)
            Messages.Add(message);
        _currentSessionId = summary.Id;
        _nextSequence = loaded.Count;
        IsHistoryPanelOpen = false;
    }

    private void DeleteSession(ChatSessionSummary? summary)
    {
        if (summary == null) return;

        try
        {
            _historyService.DeleteSession(summary.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete chat session '{summary.Id}': {ex.Message}");
            return;
        }

        if (_currentSessionId == summary.Id)
        {
            _currentSessionId = null;
            _nextSequence = 0;
            Messages.Clear();
        }
        RefreshHistoryList();
    }

    private void ResolveApproval(bool approved)
    {
        _pendingApprovalTcs?.TrySetResult(approved);
        _pendingApprovalTcs = null;
        PendingApprovalCall = null;
        PendingApprovalDescription = "";
    }

    private async Task SendMessageAsync()
    {
        if (IsStreaming || string.IsNullOrWhiteSpace(DraftInput)) return;
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl) || string.IsNullOrWhiteSpace(_settings.Model))
        {
            Messages.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                IsError = true,
                Content = "Configure a base URL and model in settings before sending a message."
            });
            IsSettingsPanelOpen = true;
            return;
        }

        var userText = DraftInput.Trim();
        DraftInput = "";
        EnsureSession(userText);
        AddMessage(new ChatMessage { Role = ChatRole.User, Content = userText });

        IsStreaming = true;
        _streamCts = new CancellationTokenSource();
        try
        {
            await RunConversationLoopAsync(_streamCts.Token);
        }
        catch (OperationCanceledException)
        {
            // User-initiated cancel; nothing further to report.
        }
        catch (Exception ex)
        {
            AddMessage(new ChatMessage { Role = ChatRole.Assistant, IsError = true, Content = ex.Message });
        }
        finally
        {
            IsStreaming = false;
            _streamCts = null;
        }
    }

    /// <summary>
    /// Creates a new persisted session (titled from the first message) the first time
    /// a real message is sent in this tab — no DB row for a tab that's opened but never used.
    /// </summary>
    private void EnsureSession(string firstUserText)
    {
        if (_currentSessionId != null) return;
        try
        {
            _currentSessionId = _historyService.CreateSession(DeriveTitle(firstUserText));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create chat history session: {ex.Message}");
        }
    }

    private static string DeriveTitle(string text)
    {
        var oneLine = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return oneLine.Length <= 48 ? oneLine : oneLine[..48] + "…";
    }

    private void AddToTranscript(ChatMessage message)
    {
        if (Dispatcher.UIThread.CheckAccess()) Messages.Add(message);
        else Dispatcher.UIThread.Post(() => Messages.Add(message));
    }

    /// <summary>
    /// Writes a message to the current session in the background, using its content/tool
    /// calls as they stand right now. Call this only once a message's content is final —
    /// for a streaming assistant reply that means after the stream completes, not when the
    /// (still-empty) placeholder is first added to the transcript.
    /// </summary>
    private void PersistMessage(ChatMessage message)
    {
        var sessionId = _currentSessionId;
        if (sessionId == null) return;

        var sequence = _nextSequence++;
        Task.Run(() =>
        {
            try
            {
                _historyService.AppendMessage(sessionId, message, sequence);
                _historyService.TouchSession(sessionId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to persist chat message: {ex.Message}");
            }
        });
    }

    /// <summary>Adds a message with already-final content to both the transcript and history.</summary>
    private void AddMessage(ChatMessage message)
    {
        AddToTranscript(message);
        PersistMessage(message);
    }

    // Not shown in the transcript — prepended to every request so the model has identity
    // and behavioral guidance instead of inferring everything from tool names alone.
    // Sent fresh with each request like the rest of the history (the Chat Completions
    // protocol is stateless; there's no server-side session to configure this once).
    // Built per-request (not a const) because the scoped-server paragraph depends on
    // SelectedServerProfile, which can change mid-conversation.
    private string BuildSystemPrompt()
    {
        var prompt =
            "You are Helm, the built-in AI copilot inside Termox, a cross-platform SSH/SFTP desktop client. " +
            "You can inspect and operate on the user's saved SSH connections using the tools provided. " +
            "Prefer read-only tools (list, read, lookup, scan, ping, hash) when they're enough to answer. " +
            "Tools that run commands or write/rename/upload files require the user's explicit approval before " +
            "they execute — expect a short pause while they approve or deny, and don't repeat the call while " +
            "waiting. Tools that delete files/keys or export secret keys are irreversible — be explicit about " +
            "exactly what will happen before calling them. Running a command with sudo is an elevated-privilege " +
            "action — only set sudo when the task genuinely requires root, and say so plainly before doing it. " +
            "Keep responses concise and use markdown (tables, code spans, bullet/numbered lists) where it " +
            "improves readability.";

        prompt += SelectedServerProfile != null
            ? $" This chat is scoped to a single server: '{SelectedServerProfile.Name}' ({SelectedServerProfile.Host}). " +
              "Every SSH/SFTP tool call automatically targets that server — you don't need and won't be given a " +
              "profileId parameter. Do not ask the user which server to use; there is only this one."
            : " Every SSH/SFTP tool needs a profileId; call list_connections first if you don't already know " +
              "it, and never guess one.";

        return prompt;
    }

    private async Task RunConversationLoopAsync(CancellationToken cancellationToken)
    {
        // Bounded so a long-running tool-call conversation can't loop forever against
        // a misbehaving endpoint.
        const int maxRounds = 8;

        // Carries context into the NEXT round's "waiting" bubble so it reads as "reviewing
        // the ssh_run_command result…" instead of a bare "thinking…" that looks identical
        // whether the model just started or has been stuck for a minute.
        var nextStatus = "Thinking…";

        for (var round = 0; round < maxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tools = _toolRegistry.BuildToolDefinitions();
            var history = TrimHistory(Messages.ToList());
            history.Insert(0, new ChatMessage { Role = ChatRole.System, Content = BuildSystemPrompt() });
            var assistantMessage = new ChatMessage { Role = ChatRole.Assistant, StatusText = nextStatus };
            var accumulator = new ChatToolCallAccumulator();
            string? finishReason = null;

            AddToTranscript(assistantMessage);

            try
            {
                await foreach (var evt in _client.StreamCompletionAsync(history, tools, _settings, cancellationToken))
                {
                    switch (evt)
                    {
                        case ChatStreamEvent.ContentDelta delta:
                            assistantMessage.Content += delta.Text;
                            break;
                        case ChatStreamEvent.ToolCallDelta toolDelta:
                            accumulator.Apply(toolDelta.Index, toolDelta.Id, toolDelta.FunctionName, toolDelta.ArgumentsFragment);
                            break;
                        case ChatStreamEvent.FinishReason reason:
                            finishReason = reason.Reason;
                            break;
                    }
                }
            }
            catch
            {
                // The request failed before (or partway through) producing content — drop
                // the empty placeholder bubble so the caller's single error message is the
                // only thing shown, instead of a blank "Helm" bubble followed by the error.
                // Never persisted (PersistMessage hasn't run yet), so nothing to clean up there.
                if (string.IsNullOrEmpty(assistantMessage.Content))
                    Dispatcher.UIThread.Post(() => Messages.Remove(assistantMessage));
                throw;
            }

            if (finishReason != "tool_calls" || !accumulator.HasAny)
            {
                // Some models occasionally finish with no tool call AND no content (often
                // right after a tool result round). Left as-is, Content stays "" forever,
                // which IsWaitingForContent reads as "still streaming" — the thinking dots
                // spin indefinitely with no way to tell it actually stopped. Surface it as
                // an error instead of a silently stuck bubble.
                if (string.IsNullOrEmpty(assistantMessage.Content))
                {
                    assistantMessage.IsError = true;
                    assistantMessage.Content = "The model finished without returning a reply. Try asking again.";
                }
                PersistMessage(assistantMessage); // Final content now that streaming is done.
                return; // Plain assistant reply — conversation round complete.
            }

            var calls = accumulator.Build();
            assistantMessage.ToolCalls = calls;
            var toolNames = string.Join(", ", calls.Select(c => c.FunctionName));
            if (string.IsNullOrEmpty(assistantMessage.Content))
                assistantMessage.Content = string.Join('\n', calls.Select(FormatToolCallLine));
            PersistMessage(assistantMessage);
            nextStatus = $"Reviewing the {toolNames} result…";

            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _toolRegistry.DispatchAsync(call, RequestApprovalAsync, cancellationToken);
                var toolMessage = new ChatMessage
                {
                    Role = ChatRole.Tool,
                    Content = result.Content,
                    ToolCallId = call.Id
                };
                AddMessage(toolMessage);
            }
        }
    }

    private Task<bool> RequestApprovalAsync(ChatToolDefinition definition, ChatToolCall call)
    {
        var tcs = new TaskCompletionSource<bool>();
        _pendingApprovalTcs = tcs;

        var verb = definition.RiskLevel == ChatToolRiskLevel.Destructive ? "PERMANENTLY" : "";
        var prefix = IsSudoCall(call) ? "⚠ WITH ROOT PRIVILEGES (sudo)," : "";
        var description = $"{prefix} Allow the assistant to {verb} run '{definition.Name}' with arguments: {call.ArgumentsJson}"
            .Replace("  ", " ").Trim();

        Dispatcher.UIThread.Post(() =>
        {
            PendingApprovalDescription = description;
            PendingApprovalCall = call;
        });

        return tcs.Task;
    }

    /// <summary>
    /// Renders a tool call the way the Claude Code CLI/extension narrates its own tool
    /// use — "● toolName(arg: value, ...)" — instead of a generic "Calling X…" placeholder,
    /// so the transcript shows exactly what's being invoked and with what.
    /// </summary>
    private static string FormatToolCallLine(ChatToolCall call) =>
        $"● {call.FunctionName}({SummarizeArgs(call.ArgumentsJson)})";

    private static string SummarizeArgs(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            var parts = doc.RootElement.EnumerateObject().Select(p => $"{p.Name}: {FormatArgValue(p.Value)}");
            return string.Join(", ", parts);
        }
        catch (System.Text.Json.JsonException)
        {
            return argumentsJson;
        }
    }

    private static string FormatArgValue(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => $"\"{Truncate(value.GetString() ?? "", 60)}\"",
        _ => value.ToString()
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static bool IsSudoCall(ChatToolCall call)
    {
        if (call.FunctionName != "ssh_run_command" || string.IsNullOrWhiteSpace(call.ArgumentsJson)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(call.ArgumentsJson);
            return doc.RootElement.TryGetProperty("sudo", out var sudo) &&
                sudo.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>Tokens reserved out of the context window for the system prompt, tool schemas, and the model's own reply.</summary>
    private const int ReservedResponseTokens = 2200;

    /// <summary>
    /// Keeps the newest messages that fit inside Settings.ContextWindowTokens (minus a
    /// reserve for the reply itself), dropping the oldest first. Always keeps at least
    /// the most recent message, even if it alone exceeds the budget.
    /// </summary>
    private List<ChatMessage> TrimHistory(List<ChatMessage> messages)
    {
        var budget = Math.Max(500, _settings.ContextWindowTokens - ReservedResponseTokens);
        var result = new List<ChatMessage>();
        var used = 0;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var tokens = EstimateMessageTokens(messages[i]);
            if (used + tokens > budget && result.Count > 0) break;
            result.Insert(0, messages[i]);
            used += tokens;
        }

        return result;
    }

    private static int EstimateMessageTokens(ChatMessage message)
    {
        var total = TokenEstimator.EstimateTokens(message.Content);
        if (message.ToolCalls != null)
            total += message.ToolCalls.Sum(c => TokenEstimator.EstimateTokens(c.ArgumentsJson));
        return total;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}
