namespace Termox.ViewModels;

/// <summary>
/// Dedicated tab shells for utilities that are also available inside the Tools workspace.
/// The shared implementation keeps both entry points behaviorally consistent.
/// </summary>
public sealed class PingTestTabViewModel : ToolsTabViewModel
{
    public PingTestTabViewModel(System.Action<ToolsTabViewModel> onClose)
        : base(onClose)
    {
        Title = "Ping Test";
    }
}

public sealed class SshKeyGeneratorTabViewModel : ToolsTabViewModel
{
    public SshKeyGeneratorTabViewModel(System.Action<ToolsTabViewModel> onClose)
        : base(onClose)
    {
        Title = "SSH Key Generator";
    }
}

public sealed class ConnectionTesterTabViewModel : ToolsTabViewModel
{
    public ConnectionTesterTabViewModel(System.Action<ToolsTabViewModel> onClose)
        : base(onClose)
    {
        Title = "Connection Tester";
    }
}

public sealed class SshEndpointTestTabViewModel : ToolsTabViewModel
{
    public SshEndpointTestTabViewModel(System.Action<ToolsTabViewModel> onClose)
        : base(onClose)
    {
        Title = "SSH Endpoint Test";
    }
}
