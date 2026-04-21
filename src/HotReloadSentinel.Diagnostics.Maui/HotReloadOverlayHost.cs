namespace HotReloadSentinel.Diagnostics.Maui;

using Microsoft.Maui.Controls;

/// <summary>
/// Hosts a <see cref="HotReloadOverlay"/> inside each window's page tree.
/// Re-binds when <see cref="Window.Page"/> swaps so navigation does not
/// drop the overlay.
/// </summary>
internal sealed class HotReloadOverlayHost
{
    public Window Window { get; }
    readonly HotReloadOverlayState _state;
    HotReloadOverlay? _overlay;
    Page? _hostedPage;

    // Track outer container subscriptions so we can detach cleanly and avoid
    // accumulating handlers when navigation re-fires TryAttach.
    Shell? _subscribedShell;
    EventHandler<ShellNavigatedEventArgs>? _shellNavigatedHandler;
    NavigationPage? _subscribedNav;
    EventHandler<NavigationEventArgs>? _navPushedHandler;
    EventHandler<NavigationEventArgs>? _navPoppedHandler;

    public HotReloadOverlayHost(Window window, HotReloadOverlayState state)
    {
        Window = window;
        _state = state;
        Window.PropertyChanged += OnWindowPropertyChanged;
        TryAttach(Window.Page);
    }

    void OnWindowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Window.Page))
        {
            Detach();
            TryAttach(Window.Page);
        }
    }

    void TryAttach(Page? page)
    {
        if (page is null) return;

        if (page is ContentPage cp)
        {
            var existing = cp.Content;
            if (existing is null) return;

            // Avoid double-wrapping if hot reload re-runs ctor.
            if (existing is Grid g && g.StyleId == OverlayHostStyleId)
            {
                _overlay = g.Children.OfType<HotReloadOverlay>().FirstOrDefault();
                _hostedPage = page;
                return;
            }

            cp.Content = null;
            var grid = new Grid { StyleId = OverlayHostStyleId };
            grid.Children.Add(existing);
            _overlay = new HotReloadOverlay(_state);
            grid.Children.Add(_overlay);
            cp.Content = grid;
            _hostedPage = page;
        }
        else if (page is Shell shell)
        {
            DetachShell();
            _shellNavigatedHandler = (_, _) => { Detach(); TryAttach(shell.CurrentPage); };
            shell.Navigated += _shellNavigatedHandler;
            _subscribedShell = shell;
            TryAttach(shell.CurrentPage);
        }
        else if (page is NavigationPage nav)
        {
            DetachNav();
            _navPushedHandler = (_, _) => { Detach(); TryAttach(nav.CurrentPage); };
            _navPoppedHandler = (_, _) => { Detach(); TryAttach(nav.CurrentPage); };
            nav.Pushed += _navPushedHandler;
            nav.Popped += _navPoppedHandler;
            _subscribedNav = nav;
            TryAttach(nav.CurrentPage);
        }
    }

    void Detach()
    {
        // Leave the wrapper grid in place — tearing it down on every nav
        // would cause layout flicker. Just clear refs; TryAttach picks up
        // the existing grid by StyleId.
        _overlay = null;
        _hostedPage = null;
        DetachShell();
        DetachNav();
    }

    void DetachShell()
    {
        if (_subscribedShell is not null && _shellNavigatedHandler is not null)
        {
            _subscribedShell.Navigated -= _shellNavigatedHandler;
        }
        _subscribedShell = null;
        _shellNavigatedHandler = null;
    }

    void DetachNav()
    {
        if (_subscribedNav is not null)
        {
            if (_navPushedHandler is not null) _subscribedNav.Pushed -= _navPushedHandler;
            if (_navPoppedHandler is not null) _subscribedNav.Popped -= _navPoppedHandler;
        }
        _subscribedNav = null;
        _navPushedHandler = null;
        _navPoppedHandler = null;
    }

    const string OverlayHostStyleId = "__hotreload_overlay_host__";
}
