using System;
using System.IO;
using System.Linq;
using Termox.Models;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ChatHistoryServiceTests
{
    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"termox-chathistory-test-{Guid.NewGuid():N}.db");

    [Fact]
    public void CreateSessionThenListSessionsReturnsIt()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var id = service.CreateSession("Test session");

            var sessions = service.ListSessions();

            var session = Assert.Single(sessions);
            Assert.Equal(id, session.Id);
            Assert.Equal("Test session", session.Title);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AppendThenLoadMessagesRoundTripsInSequenceOrder()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var sessionId = service.CreateSession("Round trip");

            service.AppendMessage(sessionId, new ChatMessage { Role = ChatRole.User, Content = "hello" }, 0);
            service.AppendMessage(sessionId, new ChatMessage { Role = ChatRole.Assistant, Content = "hi there" }, 1);

            var loaded = service.LoadMessages(sessionId);

            Assert.Equal(2, loaded.Count);
            Assert.Equal(ChatRole.User, loaded[0].Role);
            Assert.Equal("hello", loaded[0].Content);
            Assert.Equal(ChatRole.Assistant, loaded[1].Role);
            Assert.Equal("hi there", loaded[1].Content);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ToolCallsRoundTripThroughJsonColumn()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var sessionId = service.CreateSession("Tool calls");
            var message = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = "Calling ssh_run_command…",
                ToolCalls = new() { new ChatToolCall { Id = "call_1", FunctionName = "ssh_run_command", ArgumentsJson = "{\"command\":\"ls\"}" } }
            };

            service.AppendMessage(sessionId, message, 0);
            var loaded = service.LoadMessages(sessionId);

            var toolCall = Assert.Single(Assert.Single(loaded).ToolCalls!);
            Assert.Equal("call_1", toolCall.Id);
            Assert.Equal("ssh_run_command", toolCall.FunctionName);
            Assert.Equal("{\"command\":\"ls\"}", toolCall.ArgumentsJson);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MessageWithoutToolCallsRoundTripsWithNullToolCalls()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var sessionId = service.CreateSession("No tools");
            service.AppendMessage(sessionId, new ChatMessage { Role = ChatRole.User, Content = "hi" }, 0);

            var loaded = service.LoadMessages(sessionId);

            Assert.Null(Assert.Single(loaded).ToolCalls);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ErrorFlagRoundTrips()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var sessionId = service.CreateSession("Error case");
            service.AppendMessage(sessionId, new ChatMessage { Role = ChatRole.Assistant, Content = "boom", IsError = true }, 0);

            var loaded = service.LoadMessages(sessionId);

            Assert.True(Assert.Single(loaded).IsError);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DeleteSessionRemovesItAndItsMessages()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var sessionId = service.CreateSession("To delete");
            service.AppendMessage(sessionId, new ChatMessage { Role = ChatRole.User, Content = "hi" }, 0);

            service.DeleteSession(sessionId);

            Assert.Empty(service.ListSessions());
            Assert.Empty(service.LoadMessages(sessionId));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ListSessionsOrdersByMostRecentlyUpdatedFirst()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var first = service.CreateSession("First");
            var second = service.CreateSession("Second");
            service.TouchSession(first); // now first is most recently updated

            var sessions = service.ListSessions();

            Assert.Equal(first, sessions.First().Id);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OnDiskFileNeverContainsPlaintextMessageContent()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var sessionId = service.CreateSession("Secret stuff");
            service.AppendMessage(sessionId, new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = "super-secret-command-output-value",
                ToolCalls = new() { new ChatToolCall { Id = "call_1", FunctionName = "ssh_run_command", ArgumentsJson = "{\"command\":\"cat /etc/secret-shadow-file\"}" } }
            }, 0);

            var raw = File.ReadAllText(path);

            Assert.DoesNotContain("super-secret-command-output-value", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-shadow-file", raw, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UnencryptedLegacyRowIsStillReadableNotDiscarded()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var sessionId = service.CreateSession("Legacy row");

            // Simulate a row written before content encryption existed: plain text,
            // no "ENCRYPTED:"/"KEYCHAIN:" prefix.
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO chat_messages (id, session_id, sequence, role, content, tool_calls_json, tool_call_id, is_error, timestamp)
                    VALUES ($id, $sid, 0, 'User', 'plain legacy text', NULL, NULL, 0, $ts)
                    """;
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                command.Parameters.AddWithValue("$sid", sessionId);
                command.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }

            var loaded = service.LoadMessages(sessionId);

            Assert.Equal("plain legacy text", Assert.Single(loaded).Content);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RenameSessionUpdatesTitle()
    {
        var path = TempDbPath();
        try
        {
            var service = new ChatHistoryService(path);
            var sessionId = service.CreateSession("Old title");

            service.RenameSession(sessionId, "New title");

            Assert.Equal("New title", service.ListSessions().Single().Title);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
