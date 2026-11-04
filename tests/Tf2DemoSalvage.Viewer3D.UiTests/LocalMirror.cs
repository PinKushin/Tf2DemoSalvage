using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>One file at <c>maps/&lt;name&gt;.bsp</c> on localhost, held until released; 404 for anything else, the <c>.bsp.bz2</c> asked first included.</summary>
internal sealed class LocalMirror : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _path;
    private readonly byte[] _body;
    private readonly Task _release;
    private int _served;

    public LocalMirror(string mapName, byte[] body, Task release)
    {
        _path = "/maps/" + mapName + ".bsp";
        _body = body;
        _release = release;

        using (TcpListener probe = new(IPAddress.Loopback, 0))
        {
            probe.Start();
            Url = $"http://localhost:{((IPEndPoint)probe.LocalEndpoint).Port}/";
            probe.Stop();
        }

        _listener.Prefixes.Add(Url);
        _listener.Start();
        _ = Task.Run(Serve);
    }

    public string Url { get; }

    public int Served => Volatile.Read(ref _served);

    public void Dispose() => _listener.Close();

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception closed) when (closed is HttpListenerException or ObjectDisposedException)
            {
                await TestContext.Progress.WriteLineAsync("local mirror stopped: " + closed.Message).ConfigureAwait(false);
                return;
            }

            using HttpListenerResponse response = context.Response;

            if (context.Request.Url?.AbsolutePath == _path)
            {
                await _release.ConfigureAwait(false);
                Interlocked.Increment(ref _served);
                response.ContentLength64 = _body.Length;
                await response.OutputStream.WriteAsync(_body).ConfigureAwait(false);
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
    }
}
