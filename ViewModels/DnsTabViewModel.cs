using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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

public class DnsTabViewModel : INotifyPropertyChanged, ITabViewModel
{
    private string _title = "DNS Record Inspector";
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    // Shared command support
    public ICommand DisconnectCommand { get; private set; } = new RelayCommand(() => { });
    public ICommand CloseTabCommand { get; private set; } = new RelayCommand(() => { });

    // Properties
    private string _domainName = "";
    public string DomainName
    {
        get => _domainName;
        set { _domainName = value; OnPropertyChanged(); }
    }

    private string _selectedRecordType = "A";
    public string SelectedRecordType
    {
        get => _selectedRecordType;
        set { _selectedRecordType = value; OnPropertyChanged(); }
    }

    public string[] SupportedRecordTypes => DnsRecordInspector.GetSupportedRecordTypes();

    private bool _isQuerying;
    public bool IsQuerying
    {
        get => _isQuerying;
        set { _isQuerying = value; OnPropertyChanged(); }
    }

    private string _status = "Ready";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    private string _statusColor = "#4caf50";
    public string StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    public ObservableCollection<DnsRecordDisplay> QueryResults { get; } = new();

    private string _detailedResult = "";
    public string DetailedResult
    {
        get => _detailedResult;
        set { _detailedResult = value; OnPropertyChanged(); }
    }

    private long _lastQueryTimeMs;
    public long LastQueryTimeMs
    {
        get => _lastQueryTimeMs;
        set { _lastQueryTimeMs = value; OnPropertyChanged(); }
    }

    // Commands
    public ICommand QueryDnsCommand { get; }
    public ICommand QueryAllCommand { get; }
    public ICommand CopyResultCommand { get; }
    public ICommand ClearResultsCommand { get; }

    private readonly DnsRecordInspector _dnsInspector = new();

    public DnsTabViewModel(Action<DnsTabViewModel> onClose)
    {
        DisconnectCommand = new RelayCommand(() => { });
        CloseTabCommand = new RelayCommand(() => { onClose(this); });

        QueryDnsCommand = new RelayCommand(QueryDns_Execute);
        QueryAllCommand = new RelayCommand(QueryAll_Execute);
        CopyResultCommand = new RelayCommand(() => _ = CopyToClipboard(DetailedResult));
        ClearResultsCommand = new RelayCommand(ClearResults_Execute);
    }

    private void QueryDns_Execute()
    {
        if (string.IsNullOrWhiteSpace(DomainName))
        {
            Status = "Please enter a domain name";
            StatusColor = "#f44336";
            return;
        }

        IsQuerying = true;
        QueryResults.Clear();
        DetailedResult = "Querying...";

        Task.Run(async () =>
        {
            try
            {
                var recordType = ParseRecordType(SelectedRecordType);
                var result = await _dnsInspector.QueryDnsRecordAsync(DomainName, recordType);

                Dispatcher.UIThread.Post(() =>
                {
                    LastQueryTimeMs = result.QueryTimeMs;

                    if (result.Success && result.Records.Count > 0)
                    {
                        QueryResults.Clear();
                        foreach (var record in result.Records)
                        {
                            QueryResults.Add(new DnsRecordDisplay(record));
                        }

                        DetailedResult = FormatDetailedResult(result);
                        Status = $"Found {result.Records.Count} record(s) in {result.QueryTimeMs}ms";
                        StatusColor = "#4caf50";
                    }
                    else if (!result.Success)
                    {
                        Status = $"Query failed: {result.ErrorMessage}";
                        StatusColor = "#f44336";
                        DetailedResult = result.ErrorMessage;
                        QueryResults.Clear();
                    }
                    else
                    {
                        Status = "No records found";
                        StatusColor = "#ffc107";
                        DetailedResult = $"No {result.RecordType} records found for {result.Domain}";
                        QueryResults.Clear();
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Error: {ex.Message}";
                    StatusColor = "#f44336";
                    DetailedResult = $"Error: {ex.Message}";
                    QueryResults.Clear();
                });
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsQuerying = false);
            }
        });
    }

    private void QueryAll_Execute()
    {
        if (string.IsNullOrWhiteSpace(DomainName))
        {
            Status = "Please enter a domain name";
            StatusColor = "#f44336";
            return;
        }

        IsQuerying = true;
        QueryResults.Clear();
        DetailedResult = "Querying all record types...";

        Task.Run(async () =>
        {
            try
            {
                var results = await _dnsInspector.QueryAllRecordTypesAsync(DomainName);

                var allRecords = new ObservableCollection<DnsRecordDisplay>();
                var successCount = 0;
                var totalRecords = 0;
                long totalTime = 0;

                foreach (var result in results)
                {
                    if (result.Success && result.Records.Count > 0)
                    {
                        successCount++;
                        totalRecords += result.Records.Count;
                        totalTime += result.QueryTimeMs;

                        foreach (var record in result.Records)
                        {
                            allRecords.Add(new DnsRecordDisplay(record));
                        }
                    }
                }

                Dispatcher.UIThread.Post(() =>
                {
                    LastQueryTimeMs = totalTime;
                    QueryResults.Clear();

                    foreach (var record in allRecords)
                    {
                        QueryResults.Add(record);
                    }

                    if (successCount > 0)
                    {
                        DetailedResult = FormatDetailedResultBatch(results, totalRecords);
                        Status = $"Found {totalRecords} record(s) across {successCount} type(s) in {totalTime}ms";
                        StatusColor = "#4caf50";
                    }
                    else
                    {
                        Status = "No records found for any record type";
                        StatusColor = "#ffc107";
                        DetailedResult = $"No DNS records found for {DomainName}";
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Status = $"Error: {ex.Message}";
                    StatusColor = "#f44336";
                    DetailedResult = $"Error: {ex.Message}";
                    QueryResults.Clear();
                });
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsQuerying = false);
            }
        });
    }

    private void ClearResults_Execute()
    {
        DomainName = "";
        QueryResults.Clear();
        DetailedResult = "";
        Status = "Ready";
        StatusColor = "#4caf50";
        LastQueryTimeMs = 0;
    }

    private DnsRecordInspector.DnsRecordType ParseRecordType(string recordTypeStr)
    {
        return recordTypeStr switch
        {
            "A" => DnsRecordInspector.DnsRecordType.A,
            "AAAA" => DnsRecordInspector.DnsRecordType.AAAA,
            "CNAME" => DnsRecordInspector.DnsRecordType.CNAME,
            "MX" => DnsRecordInspector.DnsRecordType.MX,
            "TXT" => DnsRecordInspector.DnsRecordType.TXT,
            "NS" => DnsRecordInspector.DnsRecordType.NS,
            "SOA" => DnsRecordInspector.DnsRecordType.SOA,
            "SRV" => DnsRecordInspector.DnsRecordType.SRV,
            "PTR" => DnsRecordInspector.DnsRecordType.PTR,
            "CAA" => DnsRecordInspector.DnsRecordType.CAA,
            _ => DnsRecordInspector.DnsRecordType.A
        };
    }

    private string FormatDetailedResult(DnsRecordInspector.DnsQueryResult result)
    {
        if (!result.Success || result.Records.Count == 0)
            return $"Error: {result.ErrorMessage}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Domain: {result.Domain}");
        sb.AppendLine($"Record Type: {result.RecordType}");
        sb.AppendLine($"Query Time: {result.QueryTimeMs}ms");
        sb.AppendLine($"Records Found: {result.Records.Count}");
        sb.AppendLine(new string('-', 50));

        foreach (var record in result.Records)
        {
            sb.AppendLine($"Value: {record.Value}");
            if (!string.IsNullOrEmpty(record.Priority))
                sb.AppendLine($"  Priority: {record.Priority}");
            if (!string.IsNullOrEmpty(record.Weight))
                sb.AppendLine($"  Weight: {record.Weight}");
            if (!string.IsNullOrEmpty(record.Port))
                sb.AppendLine($"  Port: {record.Port}");
            if (!string.IsNullOrEmpty(record.TTL))
                sb.AppendLine($"  TTL: {record.TTL}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string FormatDetailedResultBatch(List<DnsRecordInspector.DnsQueryResult> results, int totalRecords)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Domain: {DomainName}");
        sb.AppendLine($"Total Records Found: {totalRecords}");
        sb.AppendLine(new string('-', 50));

        foreach (var result in results.Where(r => r.Success && r.Records.Count > 0))
        {
            sb.AppendLine($"\n{result.RecordType} Records ({result.Records.Count}):");
            foreach (var record in result.Records)
            {
                sb.AppendLine($"  {record.Value}");
                if (!string.IsNullOrEmpty(record.Priority))
                    sb.AppendLine($"    Priority: {record.Priority}");
            }
        }

        return sb.ToString();
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

public class DnsRecordDisplay
{
    public string RecordType { get; set; }
    public string Value { get; set; }
    public string Priority { get; set; }
    public string DisplayText { get; set; }

    public DnsRecordDisplay(DnsRecordInspector.DnsRecord record)
    {
        RecordType = record.RecordType;
        Value = record.Value;
        Priority = record.Priority;
        
        // Format display text based on record type
        if (!string.IsNullOrEmpty(Priority))
            DisplayText = $"[{Priority}] {Value}";
        else
            DisplayText = Value;
    }
}
