using System.Collections.Generic;
using Termox.ViewModels;

namespace Termox.Services;

/// <summary>
/// Browser-style back/forward history over which tab was selected. Kept as a plain class
/// (no Avalonia dependency) so the index math is unit-testable on its own.
/// </summary>
public class TabNavigationHistory
{
    private readonly List<ITabViewModel> _entries = new();
    private int _index = -1;

    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;

    /// <summary>
    /// Records a newly selected tab. Truncates any forward entries past the current
    /// position first, matching standard browser-history semantics — visiting a new tab
    /// after going back discards the old forward branch. A push of the tab already at the
    /// current position (e.g. a redundant SelectedTab assignment) is a no-op.
    /// </summary>
    public void Push(ITabViewModel tab)
    {
        if (_index >= 0 && _index < _entries.Count && ReferenceEquals(_entries[_index], tab)) return;

        if (_index < _entries.Count - 1)
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);

        _entries.Add(tab);
        _index = _entries.Count - 1;
    }

    public ITabViewModel? GoBack()
    {
        if (!CanGoBack) return null;
        _index--;
        return _entries[_index];
    }

    public ITabViewModel? GoForward()
    {
        if (!CanGoForward) return null;
        _index++;
        return _entries[_index];
    }

    /// <summary>
    /// Strips a closed tab out of the history entirely (not just off the ends), clamping
    /// the current index so it still points at a valid entry.
    /// </summary>
    public void Remove(ITabViewModel tab)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_entries[i], tab)) continue;
            _entries.RemoveAt(i);
            if (i < _index) _index--;
            else if (i == _index) _index--;
        }

        if (_index >= _entries.Count) _index = _entries.Count - 1;
    }
}
