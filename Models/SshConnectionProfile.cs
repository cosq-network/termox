using System;
using System.Text.Json.Serialization;

namespace Termox.Models;

public class SshConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Connection";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";

    // Runtime password; persisted profiles contain only a secure-store reference.
    public string Password { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string HostKeyFingerprint { get; set; } = "";

    [JsonIgnore]
    public bool IsConnected { get; set; }
}
