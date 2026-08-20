using System;

namespace Termox.Models;

public class RemoteTextFile
{
    public string Host { get; init; } = "";
    public string RemotePath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public long Length { get; init; } = -1;
    public DateTime LastWriteTime { get; init; }
}
