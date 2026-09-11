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
    private readonly CertbotService _certbotService = new();
    private readonly SystemdService _systemdService = new();

    public ChatToolRegistry(ObservableCollection<SshConnectionProfile> savedConnections)
    {
        _savedConnections = savedConnections;
    }

    /// <summary>
    /// When set, every SSH/SFTP tool is locked to this saved connection: the "profileId"
    /// parameter is dropped from the tool schema entirely (the model never sees it and
    /// can't supply one) and resolution always uses this profile instead. Set from the
    /// chat UI's server picker so a chat session can be scoped to a single server.
    /// </summary>
    public string? ScopedProfileId { get; set; }

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
                Execute = (_, _) =>
                {
                    var scope = ScopedProfileId == null
                        ? _savedConnections
                        : _savedConnections.Where(p => p.Id == ScopedProfileId);
                    var list = scope.ToList();
                    return Task.FromResult(ChatToolResult.Ok(
                        list.Count == 0
                            ? "No saved connections."
                            : string.Join('\n', list.Select(p => $"{p.Id}: {p.Name} ({p.Host})"))));
                }
            },
            new()
            {
                Name = "ssh_run_command",
                Description = "Run a single non-interactive shell command on a saved SSH connection and return its output. " +
                    "The connection must already have a verified host key (connected once in a Terminal/SFTP tab). " +
                    "Set 'sudo' to true to run the command with elevated (root) privileges via sudo — only do this " +
                    "when the task genuinely needs it, and never for anything the user hasn't effectively asked for.",
                ParametersSchema = ObjectSchema(
                    optionalNames: new[] { "sudo" },
                    properties: WithProfileId(
                        ("command", StringProperty("The shell command to run.")),
                        ("sudo", BooleanProperty("Run this command with elevated (root) privileges via sudo. Defaults to false.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var command = RequireString(json, "command");
                    var sudo = OptionalBool(json, "sudo");
                    var output = await _sshCommandTool.RunCommandAsync(profile, command, sudo, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "sftp_list_directory",
                Description = "List files and directories at a remote path over SFTP on a saved connection.",
                ParametersSchema = ObjectSchema(
                    WithProfileId(("remotePath", StringProperty("Absolute remote directory path, e.g. /var/log.")))),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
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
                    WithProfileId(("remotePath", StringProperty("Absolute remote file path.")))),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
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
                    WithProfileId(
                        ("localPath", StringProperty("Local file path to upload.")),
                        ("remotePath", StringProperty("Destination remote file path.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
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
                    WithProfileId(
                        ("remotePath", StringProperty("Remote file path to download.")),
                        ("localDestinationFolder", StringProperty("Local folder to download into.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
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
                    WithProfileId(
                        ("remotePath", StringProperty("Current remote path.")),
                        ("newRemotePath", StringProperty("New remote path.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
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
                    WithProfileId(
                        ("remotePath", StringProperty("Remote path.")),
                        ("octalMode", StringProperty("Permission mode as an octal string, e.g. '644' or '755'.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
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
                    WithProfileId(("remotePath", StringProperty("Remote path to delete.")))),
                RiskLevel = ChatToolRiskLevel.Destructive,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var remotePath = RequireString(json, "remotePath");
                    var message = await _sftpTool.DeleteAsync(profile, remotePath, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(message);
                }
            },
            new()
            {
                Name = "sftp_transfer_between_servers",
                Description = "Move a file directly between two saved SSH servers, without downloading it to this " +
                    "machine first. Mode 'relay' streams it through this process (works between any two servers, " +
                    "no setup needed) — use this by default. Mode 'direct' runs rsync/scp ON the source server " +
                    "targeting the destination directly; only use this if the user asks for it specifically, since " +
                    "it requires the source server to already have network access and SSH key trust to the " +
                    "destination configured — this tool never sends a stored credential into that remote command.",
                ParametersSchema = ObjectSchema(
                    optionalNames: new[] { "mode" },
                    properties: new[]
                    {
                        ("sourceProfileId", StringProperty("Saved connection profile id to transfer from. Use list_connections to see available profiles.")),
                        ("sourceRemotePath", StringProperty("Absolute file path on the source server.")),
                        ("destProfileId", StringProperty("Saved connection profile id to transfer to.")),
                        ("destRemotePath", StringProperty("Absolute destination file path on the destination server.")),
                        ("mode", StringProperty("'relay' (default, recommended) or 'direct' (advanced, requires pre-existing source-to-destination SSH trust)."))
                    }),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var sourceProfileId = RequireString(json, "sourceProfileId");
                    var sourceProfile = _savedConnections.FirstOrDefault(p => p.Id == sourceProfileId)
                        ?? throw new InvalidOperationException($"Unknown connection profile id '{sourceProfileId}'.");
                    var sourceRemotePath = RequireString(json, "sourceRemotePath");
                    var destProfileId = RequireString(json, "destProfileId");
                    var destProfile = _savedConnections.FirstOrDefault(p => p.Id == destProfileId)
                        ?? throw new InvalidOperationException($"Unknown connection profile id '{destProfileId}'.");
                    var destRemotePath = RequireString(json, "destRemotePath");
                    var mode = OptionalString(json, "mode");

                    var output = string.Equals(mode, "direct", StringComparison.OrdinalIgnoreCase)
                        ? await _sftpTool.TransferDirectAsync(sourceProfile, sourceRemotePath,
                            destProfile.Host, destProfile.Port, destProfile.Username, destRemotePath, ct).ConfigureAwait(false)
                        : await _sftpTool.TransferRelayAsync(sourceProfile, sourceRemotePath,
                            destProfile, destRemotePath, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "systemd_service_status",
                Description = "Check the status of a systemd service (unit) on a saved SSH host.",
                ParametersSchema = ObjectSchema(
                    WithProfileId(("unitName", StringProperty("Systemd unit name, e.g. nginx, docker, postgresql.")))),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var unitName = RequireString(json, "unitName");
                    var output = await _systemdService.StatusAsync(profile, unitName, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "systemd_service_start",
                Description = "Start a systemd service on a saved SSH host. Requires sudo on the target host.",
                ParametersSchema = ObjectSchema(
                    WithProfileId(("unitName", StringProperty("Systemd unit name to start.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var unitName = RequireString(json, "unitName");
                    var output = await _systemdService.StartAsync(profile, unitName, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "systemd_service_stop",
                Description = "Stop a systemd service on a saved SSH host. This can cause real downtime for " +
                    "whatever depends on it — only do this when the user has clearly asked for it. Requires sudo.",
                ParametersSchema = ObjectSchema(
                    WithProfileId(("unitName", StringProperty("Systemd unit name to stop.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var unitName = RequireString(json, "unitName");
                    var output = await _systemdService.StopAsync(profile, unitName, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "systemd_service_restart",
                Description = "Restart a systemd service on a saved SSH host. Causes a brief interruption while " +
                    "the service comes back up. Requires sudo.",
                ParametersSchema = ObjectSchema(
                    WithProfileId(("unitName", StringProperty("Systemd unit name to restart.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var unitName = RequireString(json, "unitName");
                    var output = await _systemdService.RestartAsync(profile, unitName, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "systemd_service_enable",
                Description = "Enable a systemd service to start automatically at boot on a saved SSH host. Requires sudo.",
                ParametersSchema = ObjectSchema(
                    WithProfileId(("unitName", StringProperty("Systemd unit name to enable.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var unitName = RequireString(json, "unitName");
                    var output = await _systemdService.EnableAsync(profile, unitName, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "systemd_service_disable",
                Description = "Disable a systemd service from starting automatically at boot on a saved SSH host. Requires sudo.",
                ParametersSchema = ObjectSchema(
                    WithProfileId(("unitName", StringProperty("Systemd unit name to disable.")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var unitName = RequireString(json, "unitName");
                    var output = await _systemdService.DisableAsync(profile, unitName, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
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
            },
            new()
            {
                Name = "certbot_check_status",
                Description = "Check whether certbot is installed on a saved SSH host, and its version if so.",
                ParametersSchema = ObjectSchema(WithProfileId()),
                RiskLevel = ChatToolRiskLevel.Auto,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var output = await _certbotService.CheckStatusAsync(profile, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "certbot_install",
                Description = "Install certbot on a saved SSH host, auto-detecting its package manager " +
                    "(apt/dnf/yum/apk/pacman). Requires sudo on the target host.",
                ParametersSchema = ObjectSchema(WithProfileId()),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var output = await _certbotService.InstallAsync(profile, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "certbot_list_certificates",
                Description = "List TLS certificates certbot already manages on a saved SSH host. Requires sudo.",
                ParametersSchema = ObjectSchema(WithProfileId()),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var output = await _certbotService.ListCertificatesAsync(profile, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "certbot_obtain_certificate",
                Description = "Request a TLS certificate from Let's Encrypt for one or more domains on a saved SSH " +
                    "host, via certbot. Defaults to a dry run (doesn't touch Let's Encrypt's real rate limits or " +
                    "issue a real certificate) — only set 'confirmRealRequest' to true when the user has explicitly " +
                    "asked for a real certificate, since real requests are rate-limited to 5 per domain per week. " +
                    "The target domain must already point at this host and have port 80 reachable from the " +
                    "internet (standalone/webroot plugins) or an already-configured web server (nginx/apache).",
                ParametersSchema = ObjectSchema(
                    optionalNames: new[] { "webrootPath", "confirmRealRequest" },
                    properties: WithProfileId(
                        ("domains", StringProperty("Comma-separated domain names to request the certificate for, e.g. 'example.com, www.example.com'.")),
                        ("email", StringProperty("Contact email address for the Let's Encrypt account.")),
                        ("plugin", StringProperty("Challenge plugin: Standalone, Webroot, Nginx, or Apache.")),
                        ("webrootPath", StringProperty("Webroot directory path — required only when plugin is Webroot.")),
                        ("confirmRealRequest", BooleanProperty("Set true to make a real request instead of a dry run. Defaults to false (dry run)."))
                    )),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var domains = RequireString(json, "domains")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var email = RequireString(json, "email");
                    var pluginText = RequireString(json, "plugin");
                    if (!Enum.TryParse<CertbotService.CertbotPlugin>(pluginText, ignoreCase: true, out var plugin))
                        return ChatToolResult.Fail($"Unsupported certbot plugin '{pluginText}'. Use Standalone, Webroot, Nginx, or Apache.");

                    var request = new CertbotService.CertbotObtainRequest
                    {
                        Domains = domains,
                        Email = email,
                        Plugin = plugin,
                        WebrootPath = OptionalString(json, "webrootPath"),
                        DryRun = !OptionalBool(json, "confirmRealRequest")
                    };
                    var output = await _certbotService.ObtainAsync(profile, request, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "certbot_renew_all",
                Description = "Renew all of certbot's due certificates on a saved SSH host. Defaults to a dry run — " +
                    "only set 'confirmRealRequest' to true when the user has explicitly asked for a real renewal.",
                ParametersSchema = ObjectSchema(
                    optionalNames: new[] { "confirmRealRequest" },
                    properties: WithProfileId(
                        ("confirmRealRequest", BooleanProperty("Set true to make a real renewal instead of a dry run. Defaults to false (dry run).")))),
                RiskLevel = ChatToolRiskLevel.RequiresApproval,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var dryRun = !OptionalBool(json, "confirmRealRequest");
                    var output = await _certbotService.RenewAllAsync(profile, dryRun, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            },
            new()
            {
                Name = "certbot_revoke_certificate",
                Description = "Revoke and delete a certificate certbot manages on a saved SSH host. This cannot be undone.",
                ParametersSchema = ObjectSchema(
                    WithProfileId(("certName", StringProperty("The certificate name to revoke, e.g. example.com.")))),
                RiskLevel = ChatToolRiskLevel.Destructive,
                Execute = async (args, ct) =>
                {
                    var json = ParseArgs(args);
                    var profile = ResolveProfile(json);
                    var certName = RequireString(json, "certName");
                    var output = await _certbotService.RevokeAsync(profile, certName, ct).ConfigureAwait(false);
                    return ChatToolResult.Ok(output);
                }
            }
        };
    }

    /// <summary>
    /// Prepends the "profileId" parameter to a tool's schema, unless the chat is scoped
    /// to a single server — in which case the model never needs (or is given) a choice.
    /// </summary>
    private (string Name, JsonObject Schema)[] WithProfileId(params (string Name, JsonObject Schema)[] extra)
    {
        if (ScopedProfileId != null) return extra;
        return new[] { ProfileIdProperty() }.Concat(extra).ToArray();
    }

    private SshConnectionProfile ResolveProfile(JsonElement json)
    {
        if (ScopedProfileId != null)
        {
            var scoped = _savedConnections.FirstOrDefault(p => p.Id == ScopedProfileId);
            if (scoped == null)
                throw new InvalidOperationException("The server this chat is scoped to is no longer saved.");
            return scoped;
        }

        var profileId = RequireString(json, "profileId");
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

    private static JsonObject BooleanProperty(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description
    };

    private static string OptionalString(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return "";
        return value.GetString() ?? "";
    }

    private static bool OptionalBool(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static string ObjectSchema(params (string Name, JsonObject Schema)[] properties) =>
        ObjectSchema(optionalNames: null, properties);

    private static string ObjectSchema(IReadOnlyCollection<string>? optionalNames, params (string Name, JsonObject Schema)[] properties)
    {
        var propertiesNode = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema) in properties)
        {
            propertiesNode[name] = schema;
            if (optionalNames == null || !optionalNames.Contains(name))
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
