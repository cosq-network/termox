using System;
using System.IO;
using Termox.Models;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class ChatSettingsServiceTests
{
    private static string TempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), $"termox-chatsettings-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void SaveThenLoadRoundTripsSettings()
    {
        var path = TempSettingsPath();
        try
        {
            var service = new ChatSettingsService(path);
            var settings = new ChatSettings
            {
                BaseUrl = "https://api.example.com/v1",
                ApiKey = "sk-test-secret",
                Model = "gpt-4o-mini",
                Temperature = 0.5,
                MaxHistoryMessages = 20,
                AutoApproveReadOnlyTools = false
            };

            service.Save(settings);
            var loaded = service.Load();

            Assert.Equal(settings.BaseUrl, loaded.BaseUrl);
            Assert.Equal(settings.ApiKey, loaded.ApiKey);
            Assert.Equal(settings.Model, loaded.Model);
            Assert.Equal(settings.Temperature, loaded.Temperature);
            Assert.Equal(settings.MaxHistoryMessages, loaded.MaxHistoryMessages);
            Assert.Equal(settings.AutoApproveReadOnlyTools, loaded.AutoApproveReadOnlyTools);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void OnDiskFileNeverContainsPlaintextApiKey()
    {
        var path = TempSettingsPath();
        try
        {
            var service = new ChatSettingsService(path);
            service.Save(new ChatSettings { ApiKey = "sk-super-secret-value" });

            var raw = File.ReadAllText(path);

            Assert.DoesNotContain("sk-super-secret-value", raw, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadWithoutExistingFileReturnsDefaults()
    {
        var service = new ChatSettingsService(TempSettingsPath());

        var loaded = service.Load();

        Assert.Equal("", loaded.BaseUrl);
        Assert.Equal("", loaded.ApiKey);
    }
}
