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
    private readonly ChatToolRegistry _toolRegistry;
    private readonly OpenAiChatClient _client = new();
    private readonly Action<ChatTabViewModel> _onClose;

    private CancellationTokenSource? _streamCts;
    private TaskCompletionSource<bool>? _pendingApprovalTcs;

    private string _title = "Chat";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    public ICommand DisconnectCommand { get; }
    public ICommand CloseTabCommand { get; }

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    private string _draftInput = "";
    public string DraftInput { get => _draftInput; set { _draftInput = value; OnPropertyChanged(); } }

    private bool _isStreaming;
    public bool IsStreaming { get => _isStreaming; set { _isStreaming = value; OnPropertyChanged(); } }

    private bool _isSettingsPanelOpen;
    public bool IsSettingsPanelOpen { get => _isSettingsPanelOpen; set { _isSettingsPanelOpen = value; OnPropertyChanged(); } }

    private ChatSettings _settings;
    public ChatSettings Settings { get => _settings; set { _settings = value; OnPropertyChanged(); } }

    public IReadOnlyList<ChatModelInfo> ModelOptions { get; } = ChatModelCatalog.KnownModels
        .Append(new ChatModelInfo { Id = ChatModelCatalog.CustomModelSentinel, DisplayName = "Custom…", Provider = "" })
        .ToList();

    private ChatModelInfo? _selectedModel;
    public ChatModelInfo? SelectedModel
    {
        get => _selectedModel;
        set
        {
            _selectedModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomModelSelected));
            if (value != null && value.Id != ChatModelCatalog.CustomModelSentinel)
            {
                Settings.Model = value.Id;
                Settings.ContextWindowTokens = value.ContextWindowTokens;
                OnPropertyChanged(nameof(Settings));
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
            _customModelId = value;
            OnPropertyChanged();
            if (IsCustomModelSelected)
            {
                Settings.Model = value;
                OnPropertyChanged(nameof(Settings));
            }
            OnPropertyChanged(nameof(IsModelUnrecognized));
        }
    }

    /// <summary>
    /// True when the resolved model string isn't in the curated catalog — shown as a
    /// non-blocking warning; Termox's OpenAI-compatible client will still try it as-is.
    /// </summary>
    public bool IsModelUnrecognized => !string.IsNullOrWhiteSpace(Settings.Model) && !ChatModelCatalog.IsKnownModel(Settings.Model);

    private ChatToolCall? _pendingApprovalCall;
    public ChatToolCall? PendingApprovalCall { get => _pendingApprovalCall; set { _pendingApprovalCall = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPendingApproval)); } }

    public bool HasPendingApproval => PendingApprovalCall != null;

    private string _pendingApprovalDescription = "";
    public string PendingApprovalDescription { get => _pendingApprovalDescription; set { _pendingApprovalDescription = value; OnPropertyChanged(); } }

    public ICommand SendMessageCommand { get; }
    public ICommand CancelStreamingCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ClearTranscriptCommand { get; }
    public ICommand ApproveToolCallCommand { get; }
    public ICommand DenyToolCallCommand { get; }

    public ChatTabViewModel(
        Action<ChatTabViewModel> onClose,
        ObservableCollection<SshConnectionProfile> savedConnections,
        ChatSettingsService settingsService)
    {
        _onClose = onClose;
        _settingsService = settingsService;
        _settings = _settingsService.Load();
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

        SendMessageCommand = new RelayCommand(() => _ = SendMessageAsync(), () => !IsStreaming && !string.IsNullOrWhiteSpace(DraftInput));
        CancelStreamingCommand = new RelayCommand(() => _streamCts?.Cancel());
        OpenSettingsCommand = new RelayCommand(() => IsSettingsPanelOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsPanelOpen = false);
        SaveSettingsCommand = new RelayCommand(() =>
        {
            _settingsService.Save(Settings);
            IsSettingsPanelOpen = false;
        });
        ClearTranscriptCommand = new RelayCommand(() => Messages.Clear());
        ApproveToolCallCommand = new RelayCommand(() => ResolveApproval(true));
        DenyToolCallCommand = new RelayCommand(() => ResolveApproval(false));
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
        if (string.IsNullOrWhiteSpace(Settings.BaseUrl) || string.IsNullOrWhiteSpace(Settings.Model))
        {
            Messages.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = "Configure a base URL and model in settings before sending a message."
            });
            IsSettingsPanelOpen = true;
            return;
        }

        var userText = DraftInput.Trim();
        DraftInput = "";
        Messages.Add(new ChatMessage { Role = ChatRole.User, Content = userText });

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
            Messages.Add(new ChatMessage { Role = ChatRole.Assistant, Content = $"Error: {ex.Message}" });
        }
        finally
        {
            IsStreaming = false;
            _streamCts = null;
        }
    }

    private async Task RunConversationLoopAsync(CancellationToken cancellationToken)
    {
        // Bounded so a long-running tool-call conversation can't loop forever against
        // a misbehaving endpoint.
        const int maxRounds = 8;

        for (var round = 0; round < maxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tools = _toolRegistry.BuildToolDefinitions();
            var history = TrimHistory(Messages.ToList());
            var assistantMessage = new ChatMessage { Role = ChatRole.Assistant };
            var accumulator = new ChatToolCallAccumulator();
            string? finishReason = null;

            Dispatcher.UIThread.Post(() => Messages.Add(assistantMessage));

            await foreach (var evt in _client.StreamCompletionAsync(history, tools, Settings, cancellationToken))
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

            if (finishReason != "tool_calls" || !accumulator.HasAny)
                return; // Plain assistant reply — conversation round complete.

            var calls = accumulator.Build();
            assistantMessage.ToolCalls = calls;
            if (string.IsNullOrEmpty(assistantMessage.Content))
                assistantMessage.Content = "Calling " + string.Join(", ", calls.Select(c => c.FunctionName)) + "…";

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
                Dispatcher.UIThread.Post(() => Messages.Add(toolMessage));
            }
        }
    }

    private Task<bool> RequestApprovalAsync(ChatToolDefinition definition, ChatToolCall call)
    {
        var tcs = new TaskCompletionSource<bool>();
        _pendingApprovalTcs = tcs;

        var verb = definition.RiskLevel == ChatToolRiskLevel.Destructive ? "PERMANENTLY" : "";
        var description = $"Allow the assistant to {verb} run '{definition.Name}' with arguments: {call.ArgumentsJson}".Replace("  ", " ");

        Dispatcher.UIThread.Post(() =>
        {
            PendingApprovalDescription = description;
            PendingApprovalCall = call;
        });

        return tcs.Task;
    }

    /// <summary>Tokens reserved out of the context window for the model's own reply and tool schemas.</summary>
    private const int ReservedResponseTokens = 2000;

    /// <summary>
    /// Keeps the newest messages that fit inside Settings.ContextWindowTokens (minus a
    /// reserve for the reply itself), dropping the oldest first. Always keeps at least
    /// the most recent message, even if it alone exceeds the budget.
    /// </summary>
    private List<ChatMessage> TrimHistory(List<ChatMessage> messages)
    {
        var budget = Math.Max(500, Settings.ContextWindowTokens - ReservedResponseTokens);
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
