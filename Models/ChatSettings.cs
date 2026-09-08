namespace Termox.Models;

public class ChatSettings
{
    public string BaseUrl { get; set; } = "";

    /// <summary>Stored encrypted-at-rest via CredentialManager; plaintext only while held in memory.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "";
    public double Temperature { get; set; } = 0.7;

    /// <summary>Oldest messages beyond this count are dropped from the outgoing request (0 = no limit).</summary>
    public int MaxHistoryMessages { get; set; } = 40;

    public bool AutoApproveReadOnlyTools { get; set; } = true;
}
