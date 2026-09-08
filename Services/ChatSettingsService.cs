using System;
using System.IO;
using System.Text.Json;
using Termox.Models;

namespace Termox.Services;

/// <summary>
/// Loads and saves chat feature settings (endpoint, model, API key) to
/// %AppData%\Termox\chatsettings.json, mirroring MainViewModel's connections/bookmarks
/// persistence: plain System.Text.Json, no configured options, locked around file I/O.
/// The API key is encrypted at rest via CredentialManager under a dedicated key id so
/// it can never be decrypted using any SSH-profile-derived entropy.
/// </summary>
public class ChatSettingsService
{
    private const string ApiKeyCredentialId = "chat:apiKey";
    private readonly object _persistenceLock = new();
    private readonly string _settingsPath;

    public ChatSettingsService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Termox", "chatsettings.json"))
    {
    }

    public ChatSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public ChatSettings Load()
    {
        lock (_persistenceLock)
        {
            if (!File.Exists(_settingsPath))
                return new ChatSettings();

            try
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<ChatSettings>(json) ?? new ChatSettings();
                settings.ApiKey = CredentialManager.IsEncrypted(settings.ApiKey)
                    ? CredentialManager.DecryptCredential(settings.ApiKey, ApiKeyCredentialId)
                    : "";
                return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load chat settings: {ex.Message}");
                return new ChatSettings();
            }
        }
    }

    public void Save(ChatSettings settings)
    {
        lock (_persistenceLock)
        {
            var toWrite = new ChatSettings
            {
                BaseUrl = settings.BaseUrl,
                ApiKey = CredentialManager.EncryptCredential(settings.ApiKey, ApiKeyCredentialId),
                Model = settings.Model,
                Temperature = settings.Temperature,
                ContextWindowTokens = settings.ContextWindowTokens,
                AutoApproveReadOnlyTools = settings.AutoApproveReadOnlyTools
            };

            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(toWrite));
        }
    }
}
