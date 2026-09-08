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

    private List<ChatMessage> TrimHistory(List<ChatMessage> messages)
    {
        var limit = Settings.MaxHistoryMessages;
        if (limit <= 0 || messages.Count <= limit) return messages;
        return messages.Skip(messages.Count - limit).ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}
