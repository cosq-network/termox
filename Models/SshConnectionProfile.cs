using System;
using System.Text.Json.Serialization;

namespace Termox.Models;

public class SshConnectionProfile
{
    private int _port = 22;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Connection";
    public string Host { get; set; } = "";
    public int Port
    {
        get => _port;
        set
        {
            if (value is < 1 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(Port), value, "Port must be between 1 and 65535.");
            _port = value;
        }
    }
    public string Username { get; set; } = "";

    public string Password { get; set; } = "";
    public string PrivateKeyPassphrase { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string HostKeyFingerprint { get; set; } = "";
    public DateTime LastUsed { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Seconds between SSH keep-alive messages while a terminal session is connected.
    /// 0 disables keep-alive (SSH.NET default).
    /// </summary>
    public int KeepAliveIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Minutes of inactivity before an SSH terminal session auto-disconnects.
    /// 0 keeps the session open indefinitely.
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 0;

    [JsonIgnore]
    public bool IsConnected { get; set; }
}
