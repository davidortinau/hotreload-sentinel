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
    static bool s_subscribed;
    static int s_subscribeRetries;
    // Bound the discovery retry so we don't spin forever in headless / misconfigured
    // hosts where Application.Current is never assigned. ~10s at the dispatcher's
    // typical cadence, after which we silently give up — overlay is debug-only.
    const int MaxSubscribeRetries = 200;

    // ConditionalWeakTable holds weak references to Window keys — entries are
    // automatically collected when the Window is garbage-collected, avoiding
    // the leak that a plain Dictionary<Window,_> would cause in multi-window
    // scenarios (e.g. iPad, macOS multi-window).
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, HotReloadWindowOverlay> s_overlays = new();

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

        // MAUI has no public cross-platform "WindowCreated" event at Application
        // level in all versions. PageAppearing is the most reliable hook — it
        // fires once a window has a page, which is exactly when IWindowOverlay
        // is ready to attach. We also re-seat on every fire because MVU
        // frameworks (MauiReactor) swap Window.Page on re-render, which on
        // some platforms causes the native overlay subview to be lost.
        app.PageAppearing += (_, page) =>
        {
            var w = (page as Page)?.Window ?? page?.Window;
            if (w is null) return;
            ReattachOverlay(w);
        };
    }

    static void AttachOverlay(Window window)
    {
        if (s_state is null) return;
        if (s_overlays.TryGetValue(window, out _)) { ReattachOverlay(window); return; }

        try
        {
            var overlay = new HotReloadWindowOverlay(window, s_state);
            if (window.AddOverlay(overlay))
            {
                s_overlays.Add(window, overlay);
            }
        }
        catch
        {
            // If the platform doesn't support window overlays (edge case),
            // we silently drop — the diagnostics endpoint is unaffected.
        }
    }

    static void ReattachOverlay(Window window)
    {
        if (!s_overlays.TryGetValue(window, out var overlay))
        {
            AttachOverlay(window);
            return;
        }

        try
        {
            // Remove-and-re-add forces MAUI to re-run the handler's overlay
            // update, which re-parents the native subview on top of the new
            // page tree. Idempotent if the overlay is still live.
            window.RemoveOverlay(overlay);
            window.AddOverlay(overlay);
            overlay.Invalidate();
        }
        catch { }
    }
}
#endif

