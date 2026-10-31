using System.Net;
using System.Net.Sockets;

namespace Pigeon.IntegrationTests.Steps;

internal static class TestPorts {
    public static string FreePort() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port.ToString(); }
        finally { listener.Stop(); }
    }
}
