using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Persists chat sessions and their full message history (including tool calls/results)
/// to a local SQLite database at %AppData%\Termox\termox.db, so conversations survive
/// app restarts and can be browsed/resumed from the Chat tab's History panel. Each public
/// method opens and disposes its own short-lived connection — simplest safe pattern for a
/// single-window desktop app, avoids shared-connection concurrency handling entirely.
/// </summary>
public class ChatHistoryService
{
    private const string DateFormat = "O"; // round-trippable ISO 8601

    // Distinct from "chat:apiKey" (ChatSettingsService) and every SSH profile's own key id,
    // so a leaked termox.db can't be decrypted using entropy derived from those.
    private const string ContentEncryptionKeyId = "chat:history";

    private readonly string _connectionString;

    public ChatHistoryService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Termox", "termox.db"))
    {
    }

    public ChatHistoryService(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_sessions (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chat_messages (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                tool_calls_json TEXT NULL,
                tool_call_id TEXT NULL,
                is_error INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                FOREIGN KEY (session_id) REFERENCES chat_sessions (id)
            );
            CREATE INDEX IF NOT EXISTS idx_chat_messages_session ON chat_messages (session_id, sequence);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public string CreateSession(string title)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString(DateFormat, CultureInfo.InvariantCulture);

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO chat_sessions (id, title, created_at, updated_at) VALUES ($id, $title, $now, $now)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
        return id;
    }

    public void RenameSession(string sessionId, string title)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE chat_sessions SET title = $title WHERE id = $id";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$id", sessionId);
        command.ExecuteNonQuery();
    }

    public void TouchSession(string sessionId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE chat_sessions SET updated_at = $now WHERE id = $id";
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", sessionId);
        command.ExecuteNonQuery();
    }

    public void DeleteSession(string sessionId)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var deleteMessages = connection.CreateCommand())
        {
            deleteMessages.Transaction = transaction;
            deleteMessages.CommandText = "DELETE FROM chat_messages WHERE session_id = $id";
            deleteMessages.Parameters.AddWithValue("$id", sessionId);
            deleteMessages.ExecuteNonQuery();
        }

        using (var deleteSession = connection.CreateCommand())
        {
            deleteSession.Transaction = transaction;
            deleteSession.CommandText = "DELETE FROM chat_sessions WHERE id = $id";
            deleteSession.Parameters.AddWithValue("$id", sessionId);
            deleteSession.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<ChatSessionSummary> ListSessions()
    {
        var sessions = new List<ChatSessionSummary>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, created_at, updated_at FROM chat_sessions ORDER BY updated_at DESC";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(new ChatSessionSummary
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                CreatedAt = ParseDate(reader.GetString(2)).ToLocalTime(),
                UpdatedAt = ParseDate(reader.GetString(3)).ToLocalTime()
            });
        }

        return sessions;
    }

    public void AppendMessage(string sessionId, ChatMessage message, int sequence)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_messages
                (id, session_id, sequence, role, content, tool_calls_json, tool_call_id, is_error, timestamp)
            VALUES
                ($id, $sessionId, $sequence, $role, $content, $toolCallsJson, $toolCallId, $isError, $timestamp)
            """;
        command.Parameters.AddWithValue("$id", message.Id);
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$role", message.Role.ToString());
        command.Parameters.AddWithValue("$content", EncryptField(message.Content));
        command.Parameters.AddWithValue("$toolCallsJson",
            message.ToolCalls is { Count: > 0 } ? (object)EncryptField(JsonSerializer.Serialize(message.ToolCalls)) : DBNull.Value);
        command.Parameters.AddWithValue("$toolCallId", (object?)message.ToolCallId ?? DBNull.Value);
        command.Parameters.AddWithValue("$isError", message.IsError ? 1 : 0);
        command.Parameters.AddWithValue("$timestamp", message.Timestamp.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public List<ChatMessage> LoadMessages(string sessionId)
    {
        var messages = new List<ChatMessage>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, role, content, tool_calls_json, tool_call_id, is_error, timestamp
            FROM chat_messages WHERE session_id = $id ORDER BY sequence ASC
            """;
        command.Parameters.AddWithValue("$id", sessionId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var toolCallsJson = reader.IsDBNull(3) ? null : DecryptField(reader.GetString(3));
            messages.Add(new ChatMessage
            {
                Id = reader.GetString(0),
                Role = Enum.Parse<ChatRole>(reader.GetString(1)),
                Content = DecryptField(reader.GetString(2)),
                ToolCalls = string.IsNullOrEmpty(toolCallsJson) ? null : JsonSerializer.Deserialize<List<ChatToolCall>>(toolCallsJson),
                ToolCallId = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsError = reader.GetInt32(5) != 0,
                Timestamp = ParseDate(reader.GetString(6))
            });
        }

        return messages;
    }

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string EncryptField(string value) =>
        string.IsNullOrEmpty(value) ? value : CredentialManager.EncryptCredential(value, ContentEncryptionKeyId);

    /// <summary>
    /// Decrypts a field written by EncryptField. Unlike CredentialManager's normal
    /// "refuse legacy plaintext" behavior for credentials (correct there, since a
    /// credential should never have been plaintext), a row written before this field
    /// was encrypted is legitimate history, not a security smell — so an unprefixed
    /// value is returned as-is instead of being discarded.
    /// </summary>
    private static string DecryptField(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return CredentialManager.IsEncrypted(value)
            ? CredentialManager.DecryptCredential(value, ContentEncryptionKeyId)
            : value;
    }
}
