# Chat Assistant — Feature Implementation

## Overview
Added an in-app LLM chatbot to Termox, modeled on Claude Code's editor extension: a user configures their own OpenAI-compatible inference endpoint (base URL, API key, model), and the model can call Termox's own SSH/SFTP/network tools as function-calling "tools" to inspect and operate on saved connections — instead of the user driving every action by hand. Lives in a new **Chat** tab, launched from a new sidebar section alongside Sessions/Bookmarks/Tools.

## How it works, end to end

1. **You configure an endpoint.** Sidebar → Chat → Open Chat → ⚙ Settings: base URL (e.g. `https://openrouter.ai/api/v1`, `https://api.openai.com/v1`, or a local Ollama/LM Studio address), model id, API key, temperature, and whether read-only tools auto-run. Save encrypts the key at rest and writes `%AppData%\Termox\chatsettings.json`.
2. **You type a message.** It's appended to the transcript and the full history (trimmed to `MaxHistoryMessages`) is POSTed to `{baseUrl}/chat/completions` with `stream: true` and a `tools` array describing every capability below.
3. **The response streams back token by token** over Server-Sent Events and renders live in the transcript.
4. **If the model decides to call a tool** (`finish_reason == "tool_calls"`), Termox looks the tool up, checks its risk tier, executes it, and feeds the result back to the model as a `role: "tool"` message — then loops back to step 3. This repeats (bounded at 8 rounds) until the model returns a plain answer.
5. **Risk-gated execution**: read-only tools (DNS lookup, port scan, ping, fingerprint hash, SFTP list/read, GPG public-key list) run immediately. Anything that mutates state (SSH command exec, SFTP upload/rename/chmod) or is irreversible (SFTP delete, GPG secret-key delete/export) blocks on an approval banner in the transcript — you click Approve or Deny before it runs.

## New Files Created

### Models

- **`Models/ChatMessage.cs`** — one transcript entry (`Role`, `Content`, optional `ToolCalls`/`ToolCallId`). Implements `INotifyPropertyChanged` on `Content` only, so a streaming reply can be appended to in place and the UI updates live.
- **`Models/ChatToolCall.cs`** — `Id`, `FunctionName`, raw `ArgumentsJson` string from the model.
- **`Models/ChatToolDefinition.cs`** — describes one tool: name, description, JSON-Schema parameters, `ChatToolRiskLevel` (`Auto` / `RequiresApproval` / `Destructive`), and the `Execute` delegate that runs it.
- **`Models/ChatSettings.cs`** — `BaseUrl`, `ApiKey`, `Model`, `Temperature`, `MaxHistoryMessages`, `AutoApproveReadOnlyTools`.
- **`Models/ChatToolResult.cs`** — `Success`/`Content`/`ErrorMessage`, the uniform return shape every tool produces.

### Services

- **`Services/OpenAiChatClient.cs`** — the only outbound-HTTP code in Termox. Builds the OpenAI-compatible request, reads the SSE stream, and yields `ContentDelta` / `ToolCallDelta` / `FinishReason` events. 120s request timeout, cancellable.
- **`Services/ChatSseParser.cs`** — pure static SSE-line parser (`data: {...}` / `data: [DONE]` / comments), unit-tested in isolation.
- **`Services/ChatToolCallAccumulator.cs`** — merges the model's fragmented, index-keyed `tool_calls[].function.arguments` streaming deltas into finished `ChatToolCall` records.
- **`Services/ChatToolRegistry.cs`** — builds the tool list and dispatches calls. Every SSH/SFTP tool takes a `profileId` (never raw host/port), resolved against your live saved connections — the model structurally cannot target an unpinned or arbitrary host. Tools registered: `list_connections`, `ssh_run_command`, `sftp_list_directory`, `sftp_read_text_file`, `sftp_upload_file`, `sftp_download_file`, `sftp_rename`, `sftp_chmod`, `sftp_delete`, `dns_lookup`, `port_scan`, `ping`, `calculate_fingerprint`, `gpg_list_public_keys`, `gpg_delete_key`.
- **`Services/SshCommandToolService.cs`** — runs one non-interactive command via SSH.NET's `CreateCommand().ExecuteAsync()` on a short-lived connection, separate from your interactive Terminal tab's `ShellStream` — safe to run alongside an open terminal.
- **`Services/SftpToolService.cs`** — stateless SFTP operations (list/read/upload/download/rename/chmod/delete), each opening its own short-lived `SftpClient` rather than reusing the UI-bound `SftpTabViewModel` internals.
- **`Services/NetworkDiagnostics.cs`** — small reusable `IsPortOpenAsync`/`PingOnceAsync`, extracted from the Tools tab's private handlers so both the manual UI and the chat agent share the same behavior.
- **`Services/ChatSettingsService.cs`** — loads/saves `chatsettings.json`, encrypting the API key via `CredentialManager` under a dedicated key id (`chat:apiKey`) distinct from SSH credentials.
- Reuses **`DnsRecordInspector`**, **`FingerprintUtility`**, and **`GpgKeyManager`** as-is for the corresponding tools.

### ViewModel & View

- **`ViewModels/ChatTabViewModel.cs`** — drives the send → stream → tool-dispatch loop, owns the transcript, settings-drawer state, and the tool-call approval gate (a `TaskCompletionSource<bool>` the Approve/Deny buttons complete).
- **`Views/MainWindow.axaml`** — new sidebar "Chat" section, new `Chat` `TabItem` in the main tab strip, and its `DataTemplate` (transcript list, settings drawer, approval banner, input row).
- **`ViewModels/MainViewModel.cs`** — `OpenChatTabCommand`/`OpenChatTab()`, following the same pattern as every other utility tab.

## Security model

- **Host-key pinning enforced structurally**: `SshCommandToolService`/`SftpToolService` refuse to connect if the target profile's `HostKeyFingerprint` is empty (i.e. you've never connected to it interactively) — closes a trust-on-first-use gap that would otherwise let an unattended agent call silently trust an unverified host.
- **No raw host/port ever reaches the model** — only saved, named `profileId`s.
- **Destructive actions are always gated**, regardless of the "auto-approve read-only tools" setting.
- **API key encrypted at rest** (DPAPI on Windows / OS keychain elsewhere) via the existing `CredentialManager`, under its own key id.

## Tests

`tests/Termox.Tests/`: `ChatSseParserTests`, `ChatToolCallAccumulatorTests`, `ChatSettingsServiceTests` (round-trip + on-disk plaintext-key check), `NetworkDiagnosticsTests` (real local `TcpListener`, no mocks), `ChatToolRegistryTests` (unknown profile / unpinned host / unknown tool all fail safely, not by throwing).
