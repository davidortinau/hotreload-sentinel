namespace HotReloadSentinel.Diagnostics.Maui;

using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

/// <summary>
/// Extension methods to install the in-app hot reload overlay.
/// All work is gated to DEBUG builds so release apps pay nothing.
/// </summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Adds the in-app hot reload overlay. Pairs with
    /// <c>HotReloadSentinel.Diagnostics.HotReloadDiagnosticsExtensions.UseHotReloadDiagnostics</c>
    /// — call both. Safe to call in non-DEBUG builds (no-op).
    /// </summary>
    public static MauiAppBuilder UseHotReloadOverlay(this MauiAppBuilder builder)
    {
#if DEBUG
        builder.Services.AddSingleton<HotReloadOverlayInitializer>();
        builder.Services.AddSingleton<IMauiInitializeService>(
            sp => sp.GetRequiredService<HotReloadOverlayInitializer>());
#endif
        return builder;
    }
}

#if DEBUG
internal sealed class HotReloadOverlayInitializer : IMauiInitializeService
{
    static HotReloadOverlayState? s_state;
    static readonly List<HotReloadOverlayHost> s_hosts = new();
    static bool s_subscribed;
    static int s_subscribeRetries;
    // Bound the discovery retry so we don't spin forever in headless / misconfigured
    // hosts where Application.Current is never assigned. ~10s at the dispatcher's
    // typical cadence, after which we silently give up — overlay is debug-only.
    const int MaxSubscribeRetries = 200;

    public void Initialize(IServiceProvider services)
    {
        s_state ??= new HotReloadOverlayState();

        // Application.Current may not yet be available when DI initializers
        // run on some platforms. Defer to the dispatcher; retry until set.
        TrySubscribe();
    }

    static void TrySubscribe()
    {
        var app = Application.Current;
        if (app is null)
        {
            if (Interlocked.Increment(ref s_subscribeRetries) > MaxSubscribeRetries)
            {
                // Give up — Application.Current was never set. Overlay simply
                // won't appear; diagnostics endpoint still works.
                return;
            }
            var dispatcher = Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread();
            dispatcher?.Dispatch(TrySubscribe);
            return;
        }

        if (s_subscribed) return;
        s_subscribed = true;

        foreach (var w in app.Windows) AttachOverlay(w);

        // No cross-platform "WindowCreated" event in MAUI; use PageAppearing
        // as a hook to discover newly-opened windows. Idempotent attach
        // guards against repeated firing.
        app.PageAppearing += (_, page) =>
        {
            var w = (page as Page)?.Window ?? page?.Window;
            if (w is not null) AttachOverlay(w);
        };
    }

    static void AttachOverlay(Window window)
    {
        if (s_state is null) return;
        if (s_hosts.Any(h => ReferenceEquals(h.Window, window))) return;
        s_hosts.Add(new HotReloadOverlayHost(window, s_state));
    }
}
#endif

