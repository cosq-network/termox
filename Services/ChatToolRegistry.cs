using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Builds the set of chat-agent tools and dispatches tool calls to them. Every SSH/SFTP
/// tool takes a "profileId" parameter resolved against the live saved-connections list —
/// the model is never given raw host/port fields, so it structurally cannot target an
/// unpinned or arbitrary host (see ChatHostKeyGuard for the fail-closed fingerprint check
/// applied inside SshCommandToolService/SftpToolService).
/// </summary>
public class ChatToolRegistry
{
    private readonly ObservableCollection<SshConnectionProfile> _savedConnections;
    private readonly SshCommandToolService _sshCommandTool = new();
    private readonly SftpToolService _sftpTool = new();
    private readonly DnsRecordInspector _dnsInspector = new();
    private readonly GpgKeyManager _gpgKeyManager = new();

    public ChatToolRegistry(ObservableCollection<SshConnectionProfile> savedConnections)
    {
        _savedConnections = savedConnections;
    }

    public async Task<ChatToolResult> DispatchAsync(
        ChatToolCall call,
        Func<ChatToolDefinition, ChatToolCall, Task<bool>>? requestApproval,
        CancellationToken cancellationToken = default)
    {
        var definition = BuildToolDefinitions().FirstOrDefault(d => d.Name == call.FunctionName);
        if (definition == null)
            return ChatToolResult.Fail($"Unknown tool '{call.FunctionName}'.");

        if (definition.RiskLevel != ChatToolRiskLevel.Auto)
        {
            var approved = requestApproval == null || await requestApproval(definition, call).ConfigureAwait(false);
            if (!approved)
                return ChatToolResult.Fail($"Tool call '{definition.Name}' was denied.");
        }

        try
        {
            return await definition.Execute(call.ArgumentsJson, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ChatToolResult.Fail(ex.Message);
        }
    }

    public List<ChatToolDefinition> BuildToolDefinitions()
    {
        return new List<ChatToolDefinition>
        {
            new()
            {
                Name = "list_connections",
                Description = "List the saved SSH connection profiles available to other tools, by id and name.",
                ParametersSchema = ObjectSchema(),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = (_, _) => Task.FromResult(ChatToolResult.Ok(
                    _savedConnections.Count == 0
                        ? "No saved connections."
                        : string.Join('\n', _savedConnections.Select(p => $"{p.Id}: {p.Name} ({p.Host})"))))
            },
            new()
            {
                Name = "ssh_run_command",
                Description = "Run a single non-interactive shell command on a saved SSH connection and return its output. " +
                    "The connection must already have a verified host key (connected once in a Terminal/SFTP tab).",
                ParametersSchema = ObjectSchema(
                    ProfileIdProperty(),
                    ("command", StringProperty("The shell command to run."))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json, "profileId");
                    var command = RequireString(json, "command");
                    var output = await _sshCommandTool.RunCommandAsync(profile, command, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "sftp_list_directory",
                Description = "List files and directories at a remote path over SFTP on a saved connection.",
                ParametersSchema = ObjectSchema(
                    ProfileIdProperty(),
                    ("remotePath", StringProperty("Absolute remote directory path, e.g. /var/log."))),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json, "profileId");
                    var remotePath = RequireString(json, "remotePath");
                    var listing = await _sftpTool.ListDirectoryAsync(profile, remotePath, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(listing);
                }
            },
            new()
            {
                Name = "sftp_read_text_file",
                Description = "Read a remote text file's contents over SFTP (limited to small files).",
                ParametersSchema = ObjectSchema(
                    ProfileIdProperty(),
                    ("remotePath", StringProperty("Absolute remote file path."))),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json, "profileId");
                    var remotePath = RequireString(json, "remotePath");
                    var content = await _sftpTool.ReadTextFileAsync(profile, remotePath, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(content);
                }
            },
            new()
            {
                Name = "sftp_upload_file",
                Description = "Upload a local file to a remote path over SFTP.",
                ParametersSchema = ObjectSchema(
                    ProfileIdProperty(),
                    ("localPath", StringProperty("Local file path to upload.")),
                    ("remotePath", StringProperty("Destination remote file path."))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json, "profileId");
                    var localPath = RequireString(json, "localPath");
                    var remotePath = RequireString(json, "remotePath");
                    var message = await _sftpTool.UploadFileAsync(profile, localPath, remotePath, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(message);
                }
            },
            new()
            {
                Name = "sftp_download_file",
                Description = "Download a remote file to a local destination folder over SFTP.",
                ParametersSchema = ObjectSchema(
                    ProfileIdProperty(),
                    ("remotePath", StringProperty("Remote file path to download.")),
                    ("localDestinationFolder", StringProperty("Local folder to download into."))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json, "profileId");
                    var remotePath = RequireString(json, "remotePath");
                    var localFolder = RequireString(json, "localDestinationFolder");
                    var message = await _sftpTool.DownloadFileAsync(profile, remotePath, localFolder, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(message);
                }
            },
            new()
            {
                Name = "sftp_rename",
                Description = "Rename or move a remote file/directory over SFTP.",
                ParametersSchema = ObjectSchema(
                    ProfileIdProperty(),
                    ("remotePath", StringProperty("Current remote path.")),
                    ("newRemotePath", StringProperty("New remote path."))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json, "profileId");
                    var remotePath = RequireString(json, "remotePath");
                    var newRemotePath = RequireString(json, "newRemotePath");
                    var message = await _sftpTool.RenameAsync(profile, remotePath, newRemotePath, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(message);
                }
            },
            new()
            {
                Name = "sftp_chmod",
                Description = "Change Unix permissions (octal mode, e.g. 644) on a remote file over SFTP.",
                ParametersSchema = ObjectSchema(
                    ProfileIdProperty(),
                    ("remotePath", StringProperty("Remote path.")),
                    ("octalMode", StringProperty("Permission mode as an octal string, e.g. '644' or '755'."))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json, "profileId");
                    var remotePath = RequireString(json, "remotePath");
                    var octalMode = RequireString(json, "octalMode");
                    var mode = Convert.ToInt16(octalMode, 8);
                    var message = await _sftpTool.ChmodAsync(profile, remotePath, mode, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(message);
                }
            },
            new()
            {
                Name = "sftp_delete",
                Description = "Permanently delete a remote file or directory over SFTP. This cannot be undone.",
                ParametersSchema = ObjectSchema(
                    ProfileIdProperty(),
                    ("remotePath", StringProperty("Remote path to delete."))),
                RiskLevel = ChatToolRiskLevel.Destructive,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json, "profileId");
                    var remotePath = RequireString(json, "remotePath");
                    var message = await _sftpTool.DeleteAsync(profile, remotePath, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(message);
                }
            },
            new()
            {
                Name = "dns_lookup",
                Description = "Query a single DNS record type (A, AAAA, CNAME, MX, TXT, NS, SOA, SRV, PTR, CAA) for a domain.",
                ParametersSchema = ObjectSchema(
                    ("domain", StringProperty("Domain name to query.")),
                    ("recordType", StringProperty("DNS record type, e.g. A, MX, TXT."))),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (args, _) =>
                {
                    var json = ParseArgs(args);
                    var domain = RequireString(json, "domain");
                    var recordTypeText = RequireString(json, "recordType");
                    if (!Enum.TryParse<DnsRecordInspector.DnsRecordType>(recordTypeText, ignoreCase: true, out var recordType))
                        return ChatToolResult.Fail($"Unsupported DNS record type '{recordTypeText}'.");

                    var result = await _dnsInspector.QueryDnsRecordAsync(domain, recordType).ConfigureAwait(false);
                    if (!result.Success)
                        return ChatToolResult.Fail(result.ErrorMessage);

                    return ChatToolResult.Ok(string.Join('\n', result.Records.Select(r => r.Value)));
                }
            },
            new()
            {
                Name = "port_scan",
                Description = "Check whether a single TCP port is open on a host.",
                ParametersSchema = ObjectSchema(
                    ("host", StringProperty("Hostname or IP address.")),
                    ("port", IntegerProperty("TCP port number, 1-65535."))),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var host = RequireString(json, "host");
                    var port = RequireInt(json, "port");
                    var isOpen = await NetworkDiagnostics.IsPortOpenAsync(host, port, cancellationToken: ct).ConfigureAwait(false);
                    return ChatToolResult.Ok($"{host}:{port} is {(isOpen ? "OPEN" : "CLOSED")}.");
                }
            },
            new()
            {
                Name = "ping",
                Description = "Send a single ICMP ping to a host and report round-trip time.",
                ParametersSchema = ObjectSchema(("host", StringProperty("Hostname or IP address."))),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (args, _) =>
                {
                    var json = ParseArgs(args);
                    var host = RequireString(json, "host");
                    var outcome = await NetworkDiagnostics.PingOnceAsync(host).ConfigureAwait(false);
                    return ChatToolResult.Ok(outcome.Success
                        ? $"{host}: {outcome.RoundtripMs}ms"
                        : $"{host}: unreachable ({outcome.Status})");
                }
            },
            new()
            {
                Name = "calculate_fingerprint",
                Description = "Calculate a hash fingerprint (MD5, SHA1, SHA256, SHA384, SHA512) of a text string.",
                ParametersSchema = ObjectSchema(
                    ("text", StringProperty("Text to hash.")),
                    ("algorithm", StringProperty("Hash algorithm: MD5, SHA1, SHA256, SHA384, or SHA512."))),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = (args, _) =>
                {
                    var json = ParseArgs(args);
                    var text = RequireString(json, "text");
                    var algorithmText = RequireString(json, "algorithm");
                    if (!Enum.TryParse<FingerprintUtility.HashAlgorithmType>(algorithmText, ignoreCase: true, out var algorithm))
                        return Task.FromResult(ChatToolResult.Fail($"Unsupported algorithm '{algorithmText}'."));

                    var result = FingerprintUtility.CalculateFingerprint(text, algorithm);
                    return Task.FromResult(ChatToolResult.Ok(result.FingerprintFormatted));
                }
            },
            new()
            {
                Name = "gpg_list_public_keys",
                Description = "List public GPG keys in the local keyring.",
                ParametersSchema = ObjectSchema(),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (_, _) =>
                {
                    var keys = await _gpgKeyManager.ListPublicKeysAsync().ConfigureAwait(false);
                    return ChatToolResult.Ok(keys.Count == 0
                        ? "No public keys found."
                        : string.Join('\n', keys.Select(k => $"{k.KeyId}  {k.UserId}  {k.KeyType}/{k.KeySize}")));
                }
            },
            new()
            {
                Name = "gpg_delete_key",
                Description = "Permanently delete a GPG key from the local keyring. This cannot be undone.",
                ParametersSchema = ObjectSchema(("keyId", StringProperty("The key id to delete."))),
                RiskLevel = ChatToolRiskLevel.Destructive,
                Execute = async (args, _) =>
                {
                    var json = ParseArgs(args);
                    var keyId = RequireString(json, "keyId");
                    var (success, message) = await _gpgKeyManager.DeleteKeyAsync(keyId).ConfigureAwait(false);
                    return success ? ChatToolResult.Ok(message) : ChatToolResult.Fail(message);
                }
            }
        };
    }

    private SshConnectionProfile ResolveProfile(JsonElement json, string propertyName)
    {
        var profileId = RequireString(json, propertyName);
        var profile = _savedConnections.FirstOrDefault(p => p.Id == profileId);
        if (profile == null)
            throw new InvalidOperationException($"Unknown connection profile id '{profileId}'. Use list_connections to see available profiles.");
        return profile;
    }

    private static JsonElement ParseArgs(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return JsonDocument.Parse("{}").RootElement;
        return JsonDocument.Parse(argumentsJson).RootElement;
    }

    private static string RequireString(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"Missing required argument '{propertyName}'.");
        return value.GetString() ?? "";
    }

    private static int RequireInt(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var value))
            throw new ArgumentException($"Missing required argument '{propertyName}'.");
        return value.ValueKind == JsonValueKind.Number ? value.GetInt32() : int.Parse(value.GetString() ?? "0");
    }

    private (string Name, JsonObject Schema) ProfileIdProperty()
    {
        var description = _savedConnections.Count == 0
            ? "Saved connection profile id. No connections are currently saved."
            : "Saved connection profile id. Available: " +
              string.Join(", ", _savedConnections.Select(p => $"{p.Id} ({p.Name})"));
        return ("profileId", StringProperty(description));
    }

    private static JsonObject StringProperty(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description
    };

    private static JsonObject IntegerProperty(string description) => new()
    {
        ["type"] = "integer",
        ["description"] = description
    };

    private static string ObjectSchema(params (string Name, JsonObject Schema)[] properties)
    {
        var propertiesNode = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema) in properties)
        {
            propertiesNode[name] = schema;
            required.Add(name);
        }

        var root = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = propertiesNode,
            ["required"] = required
        };
        return root.ToJsonString();
    }
}
