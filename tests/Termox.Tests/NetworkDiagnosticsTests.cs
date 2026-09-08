using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Termox.Services;
using Xunit;

namespace Termox.Tests;

public class NetworkDiagnosticsTests
{
    [Fact]
    public async Task IsPortOpenAsyncDetectsOpenLocalPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var isOpen = await NetworkDiagnostics.IsPortOpenAsync("127.0.0.1", port, timeoutMs: 2000);
            Assert.True(isOpen);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task IsPortOpenAsyncDetectsClosedLocalPort()
    {
        // Bind and immediately release a port so it is very likely unused, then probe it.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var isOpen = await NetworkDiagnostics.IsPortOpenAsync("127.0.0.1", port, timeoutMs: 500);

        Assert.False(isOpen);
    }
}
