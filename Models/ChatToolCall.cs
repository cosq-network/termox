namespace Termox.Models;

public class ChatToolCall
{
    public string Id { get; set; } = "";
    public string FunctionName { get; set; } = "";
    public string ArgumentsJson { get; set; } = "";
}
