using System;
using System.Reflection;
using System.Windows.Input;

namespace Termox.ViewModels;

public class AboutDialogViewModel
{
    public string ApplicationName { get; } = "Termox";
    public string ApplicationVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
    public string ReleaseDate { get; } = "July 2026";
    
    public string CompanyName { get; } = "The Termox Project";
    public string CompanyWebsite { get; } = "";
    public string CompanyMotto { get; } = "A focused SSH and SFTP workspace for modern remote operations.";
    
    public string ApplicationDescription { get; } = 
        "Termox is an enterprise-grade SSH and SFTP client built with modern UI/UX principles. " +
        "Combining terminal emulation, SFTP file management, and advanced network utilities into a single, elegant application.";
    
    public string TechStack { get; } = 
        "Built with Avalonia UI, SSH.NET, and .NET 10.0\n" +
        "Cross-platform support: Windows, macOS, Linux";
    
    public string License { get; } = "MIT License - © 2026 The Termox Project";
    
    public string Features { get; } = 
        "✓ SSH Terminal Access\n" +
        "✓ SFTP File Management\n" +
        "✓ Password Encryption\n" +
        "✓ Network Utilities\n" +
        "✓ Bulk File Operations\n" +
        "✓ Keyboard Shortcuts\n" +
        "✓ Context Menus\n" +
        "✓ File Permissions Editor";

    public ICommand OpenWebsiteCommand { get; }
    public ICommand OpenEmailCommand { get; }

    public AboutDialogViewModel()
    {
        OpenWebsiteCommand = new RelayCommand(() =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(CompanyWebsite)) OpenUrl(CompanyWebsite);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open website: {ex.Message}");
            }
        });

        OpenEmailCommand = new RelayCommand(() =>
        {
            try
            {
                Console.WriteLine("Termox support contact is not configured.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open email: {ex.Message}");
            }
        });
    }

    private void OpenUrl(string url)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
    }
}
