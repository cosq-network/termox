namespace Termox.Models;

public class ChatSettings
{
    public string BaseUrl { get; set; } = "";

    /// <summary>Stored encrypted-at-rest via CredentialManager; plaintext only while held in memory.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "";
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// The model's context window in tokens. Outgoing history is trimmed (oldest first)
    /// to fit inside this budget, using TokenEstimator's rough token count. Defaults to a
    /// conservative 128k; selecting a known model in the settings drawer overwrites this
    /// with that model's real context window.
    /// </summary>
    public int ContextWindowTokens { get; set; } = 128_000;

    public bool AutoApproveReadOnlyTools { get; set; } = true;
}
