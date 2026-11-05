using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// A loopback relay to one upstream address, for a dependency that must be up while a host starts and gone afterwards.
/// Bytes pass through both ways, unparsed. <see cref="Cut"/> closes the listener and drops every relayed connection, so the
/// port is then refused, which is how a database host that has gone away answers.
/// </summary>
public sealed class TcpRelay : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentDictionary<TcpClient, byte> open = new();
    private readonly CancellationTokenSource cut = new();
    private readonly string upstreamHost;
    private readonly int upstreamPort;

    public TcpRelay(string upstreamHost, int upstreamPort)
    {
        this.upstreamHost = upstreamHost;
        this.upstreamPort = upstreamPort;
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = AcceptAsync();
    }

    /// <summary>The loopback port to connect to instead of the upstream.</summary>
    public int Port { get; }

    public void Cut()
    {
        cut.Cancel();
        listener.Stop();
        foreach (TcpClient connection in open.Keys)
        {
            connection.Dispose();
        }
    }

    public void Dispose()
    {
        Cut();
        cut.Dispose();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                _ = RelayAsync(await listener.AcceptTcpClientAsync(cut.Token));
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Cut: the listener is closed.
        }
    }

    private async Task RelayAsync(TcpClient downstream)
    {
        using TcpClient upstream = new();
        open[downstream] = 0;
        open[upstream] = 0;
        try
        {
            using (downstream)
            {
                await upstream.ConnectAsync(upstreamHost, upstreamPort, cut.Token);
                NetworkStream toClient = downstream.GetStream();
                NetworkStream toServer = upstream.GetStream();
                await Task.WhenAny(toClient.CopyToAsync(toServer, cut.Token), toServer.CopyToAsync(toClient, cut.Token));
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // Either side closed, or the relay was cut.
        }
        finally
        {
            open.TryRemove(downstream, out _);
            open.TryRemove(upstream, out _);
        }
    }
}
