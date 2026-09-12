namespace Termox.Models;

public class ChatToolResult
{
    public bool Success { get; set; }
    public string Content { get; set; } = "";
    public string? ErrorMessage { get; set; }

    public static ChatToolResult Ok(string content) => new() { Success = true, Content = content };

    public static ChatToolResult Fail(string errorMessage) =>
        new() { Success = false, Content = errorMessage, ErrorMessage = errorMessage };
}
