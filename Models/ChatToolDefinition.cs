using System;
using System.Threading;
using System.Threading.Tasks;

namespace Termox.Models;

public enum ChatToolRiskLevel
{
    /// <summary>Read-only tool. Runs immediately without user confirmation.</summary>
    Auto,
    /// <summary>Mutates remote state (runs a command, writes/renames a file). Requires a click-to-approve step.</summary>
    RequiresApproval,
    /// <summary>Irreversible or exfiltration-risk (delete, secret-key export). Requires approval with stronger warning copy.</summary>
    Destructive
}

/// <summary>
/// Describes one function-calling tool exposed to the chat model: both the JSON-Schema
/// shape sent in the outgoing "tools" array and the local dispatch entry that executes it.
/// </summary>
public class ChatToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Raw JSON Schema (as a string) describing the function's parameters object.</summary>
    public required string ParametersSchema { get; init; }

    public ChatToolRiskLevel RiskLevel { get; init; } = ChatToolRiskLevel.Auto;

    /// <summary>Invoked with the raw arguments JSON string from the model; returns the tool result text.</summary>
    public required Func<string, CancellationToken, Task<ChatToolResult>> Execute { get; init; }
}
