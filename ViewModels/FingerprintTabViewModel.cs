using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Termox.Services;

namespace Termox.ViewModels;

public class FingerprintTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private string _title = "Fingerprint Utilities";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    // Shared command support
    public ICommand DisconnectCommand { get; private set; } = new RelayCommand(() => { });
    public ICommand CloseTabCommand { get; private set; } = new RelayCommand(() => { });

    // String input properties
    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set { _inputText = value; OnPropertyChanged(); }
    }

    private string _selectedAlgorithm = "SHA256";
    public string SelectedAlgorithm
    {
        get => _selectedAlgorithm;
        set { _selectedAlgorithm = value; OnPropertyChanged(); }
    }

    private string _calculatedFingerprint = "";
    public string CalculatedFingerprint
    {
        get => _calculatedFingerprint;
        set { _calculatedFingerprint = value; OnPropertyChanged(); }
    }

    private string _formattedFingerprint = "";
    public string FormattedFingerprint
    {
        get => _formattedFingerprint;
        set { _formattedFingerprint = value; OnPropertyChanged(); }
    }

    private string _sshStyleFingerprint = "";
    public string SshStyleFingerprint
    {
        get => _sshStyleFingerprint;
        set { _sshStyleFingerprint = value; OnPropertyChanged(); }
    }

    private bool _isCalculating;
    public bool IsCalculating
    {
        get => _isCalculating;
        set { _isCalculating = value; OnPropertyChanged(); }
    }

    private string _calculationStatus = "";
    public string CalculationStatus
    {
        get => _calculationStatus;
        set { _calculationStatus = value; OnPropertyChanged(); }
    }

    private string _calculationStatusColor = "#999999";
    public string CalculationStatusColor
    {
        get => _calculationStatusColor;
        set { _calculationStatusColor = value; OnPropertyChanged(); }
    }

    // Comparison properties
    private string _expectedFingerprint = "";
    public string ExpectedFingerprint
    {
        get => _expectedFingerprint;
        set { _expectedFingerprint = value; OnPropertyChanged(); }
    }

    private bool _fingerprintsMatch;
    public bool FingerprintsMatch
    {
        get => _fingerprintsMatch;
        set { _fingerprintsMatch = value; OnPropertyChanged(); }
    }

    private string _comparisonStatus = "";
    public string ComparisonStatus
    {
        get => _comparisonStatus;
        set { _comparisonStatus = value; OnPropertyChanged(); }
    }

    private string _comparisonStatusColor = "#999999";
    public string ComparisonStatusColor
    {
        get => _comparisonStatusColor;
        set { _comparisonStatusColor = value; OnPropertyChanged(); }
    }

    public string[] SupportedAlgorithms => FingerprintUtility.GetSupportedAlgorithms();

    // Commands
    public ICommand CalculateFingerprintCommand { get; }
    public ICommand CompareFingerprintsCommand { get; }
    public ICommand CopyCalculatedCommand { get; }
    public ICommand CopyFormattedCommand { get; }
    public ICommand CopySshStyleCommand { get; }
    public ICommand ClearCommand { get; }

    public FingerprintTabViewModel(Action<FingerprintTabViewModel> onClose)
    {
        DisconnectCommand = new RelayCommand(() => { });
        CloseTabCommand = new RelayCommand(() => { onClose(this); });

        CalculateFingerprintCommand = new RelayCommand(CalculateFingerprint_Execute);
        CompareFingerprintsCommand = new RelayCommand(CompareFingerprints_Execute);
        CopyCalculatedCommand = new RelayCommand(() => _ = CopyToClipboard(CalculatedFingerprint));
        CopyFormattedCommand = new RelayCommand(() => _ = CopyToClipboard(FormattedFingerprint));
        CopySshStyleCommand = new RelayCommand(() => _ = CopyToClipboard(SshStyleFingerprint));
        ClearCommand = new RelayCommand(Clear_Execute);
    }

    private void CalculateFingerprint_Execute()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            CalculatedFingerprint = "";
            FormattedFingerprint = "";
            SshStyleFingerprint = "";
            CalculationStatus = "Please enter text to calculate a fingerprint.";
            CalculationStatusColor = "#ffc107";
            return;
        }

        CalculationStatus = "";
        IsCalculating = true;
        Task.Run(() =>
        {
            try
            {
                var algorithm = ParseAlgorithm(SelectedAlgorithm);
                var result = FingerprintUtility.CalculateFingerprint(InputText, algorithm);

                Dispatcher.UIThread.Post(() =>
                {
                    CalculatedFingerprint = result.Fingerprint;
                    FormattedFingerprint = result.FingerprintFormatted;
                    SshStyleFingerprint = FingerprintUtility.FormatFingerprintSshStyle(result.Fingerprint);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    CalculatedFingerprint = "";
                    FormattedFingerprint = "";
                    SshStyleFingerprint = "";
                    CalculationStatus = $"Error: {ex.Message}";
                    CalculationStatusColor = "#f44336";
                });
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsCalculating = false);
            }
        });
    }

    private void CompareFingerprints_Execute()
    {
        if (string.IsNullOrWhiteSpace(CalculatedFingerprint) || string.IsNullOrWhiteSpace(ExpectedFingerprint))
        {
            ComparisonStatus = "Please calculate a fingerprint and enter an expected value";
            ComparisonStatusColor = "#ffc107";
            FingerprintsMatch = false;
            return;
        }

        try
        {
            var match = FingerprintUtility.CompareFingerprints(CalculatedFingerprint, ExpectedFingerprint);
            FingerprintsMatch = match;
            ComparisonStatus = match ? "✓ Fingerprints match!" : "✗ Fingerprints do not match";
            ComparisonStatusColor = match ? "#4caf50" : "#f44336";
        }
        catch (Exception ex)
        {
            ComparisonStatus = $"Error: {ex.Message}";
            ComparisonStatusColor = "#f44336";
            FingerprintsMatch = false;
        }
    }

    private void Clear_Execute()
    {
        InputText = "";
        ExpectedFingerprint = "";
        CalculatedFingerprint = "";
        FormattedFingerprint = "";
        SshStyleFingerprint = "";
        ComparisonStatus = "";
        ComparisonStatusColor = "#999999";
        FingerprintsMatch = false;
        CalculationStatus = "";
        CalculationStatusColor = "#999999";
    }

    private FingerprintUtility.HashAlgorithmType ParseAlgorithm(string algorithm)
    {
        return algorithm switch
        {
            "MD5" => FingerprintUtility.HashAlgorithmType.MD5,
            "SHA1" => FingerprintUtility.HashAlgorithmType.SHA1,
            "SHA256" => FingerprintUtility.HashAlgorithmType.SHA256,
            "SHA384" => FingerprintUtility.HashAlgorithmType.SHA384,
            "SHA512" => FingerprintUtility.HashAlgorithmType.SHA512,
            _ => FingerprintUtility.HashAlgorithmType.SHA256
        };
    }

    private async Task CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);
        if (Dispatcher.UIThread.CheckAccess()) PropertyChanged?.Invoke(this, args);
        else Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, args));
    }
}
