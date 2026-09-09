using System;

namespace Termox.Models;

/// <summary>Lightweight row for the session-history list — no message bodies.</summary>
public class ChatSessionSummary
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
