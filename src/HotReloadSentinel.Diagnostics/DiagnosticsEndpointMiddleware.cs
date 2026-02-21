namespace HotReloadSentinel.Diagnostics;

using System.Net;
using System.Text.Json;

/// <summary>
/// Lightweight HTTP listener providing a /heartbeat endpoint for the sentinel.
/// Runs on a random available port. Returns JSON with pid, updateCount, and timestamp.
/// </summary>
public sealed class DiagnosticsEndpointMiddleware : IDisposable
{
    readonly HttpListener _listener;
    readonly CancellationTokenSource _cts = new();
    readonly Task _listenTask;
    bool _disposed;

    public int Port { get; }

    public DiagnosticsEndpointMiddleware()
    {
        // Find a random available port
        var tempListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tempListener.Start();
        Port = ((IPEndPoint)tempListener.LocalEndpoint).Port;
        tempListener.Stop();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _listenTask = Task.Run(ListenLoop);
    }

    async Task ListenLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                await HandleRequest(context);
            }
            catch (HttpListenerException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Swallow transient errors, keep listening
            }
        }
    }

    static async Task HandleRequest(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";

        if (path == "/heartbeat" || path == "/")
        {
            var payload = new
            {
                pid = Environment.ProcessId,
                updateCount = MetadataUpdateCounter.UpdateCount,
                lastUpdateTimestampUtc = MetadataUpdateCounter.LastUpdateUtc == DateTime.MinValue
                    ? null
                    : MetadataUpdateCounter.LastUpdateUtc.ToString("o"),
                uptimeSeconds = (int)(DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
            };

            var json = JsonSerializer.Serialize(payload);
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 200;
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
        }
        else
        {
            context.Response.StatusCode = 404;
        }

        context.Response.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _listener.Close();
        try { _listenTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
