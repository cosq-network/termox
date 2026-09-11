using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Termox.Services;

/// <summary>
/// Loads and saves the user's chosen order for the sidebar Tools list, to
/// %AppData%\Termox\toolsorder.json — same persistence pattern as ChatSettingsService
/// (plain System.Text.Json, no configured options, falls back cleanly on any read error
/// or missing file rather than ever blocking startup).
/// </summary>
public class ToolsOrderService
{
    private readonly object _persistenceLock = new();
    private readonly string _orderPath;

    public ToolsOrderService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Termox", "toolsorder.json"))
    {
    }

    public ToolsOrderService(string orderPath)
    {
        _orderPath = orderPath;
    }

    public List<string> Load()
    {
        lock (_persistenceLock)
        {
            if (!File.Exists(_orderPath))
                return new List<string>();

            try
            {
                var json = File.ReadAllText(_orderPath);
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load tools order: {ex.Message}");
                return new List<string>();
            }
        }
    }

    public void Save(IEnumerable<string> orderedIds)
    {
        lock (_persistenceLock)
        {
            var directory = Path.GetDirectoryName(_orderPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_orderPath, JsonSerializer.Serialize(orderedIds.ToList()));
        }
    }

    /// <summary>
    /// Merges a saved order against the current tool catalog: saved ids come first (in
    /// their saved order, filtered to ones still present in the catalog — a tool removed
    /// in a later update just drops out silently instead of erroring), then any catalog
    /// ids not present in the saved order are appended at the end in catalog order (a
    /// *new* tool added in a later update doesn't vanish just because an old save
    /// predates it — it shows up at the end instead of being hidden).
    /// </summary>
    internal static List<string> ApplySavedOrder(IReadOnlyList<string> catalogIds, IReadOnlyList<string> savedOrder)
    {
        var catalogSet = new HashSet<string>(catalogIds);
        var result = new List<string>(catalogIds.Count);

        foreach (var id in savedOrder)
        {
            if (catalogSet.Contains(id) && !result.Contains(id))
                result.Add(id);
        }

        foreach (var id in catalogIds)
        {
            if (!result.Contains(id))
                result.Add(id);
        }

        return result;
    }
}
