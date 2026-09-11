using System.Windows.Input;

namespace Termox.Models;

/// <summary>
/// One row in the sidebar Tools list. Only <see cref="Id"/> is ever persisted (the saved
/// display order references tools by id) — title/description/icon/command come from the
/// fixed catalog built in MainViewModel, not from user data.
/// </summary>
public class ToolMenuItem
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string IconGlyph { get; init; } = "";
    public ICommand Command { get; init; } = null!;
}
