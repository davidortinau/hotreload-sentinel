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

        // The overlay must live inside an absolute-style container so it
        // can sit in front of the page content. Pages already provide a
        // Layout container if they're ContentPage; for the MVP we wrap
        // the existing content in a Grid only if necessary.
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
        // Other page kinds (NavigationPage, FlyoutPage, Shell, TabbedPage)
        // auto-route via their CurrentPage; subscribe and re-attach.
        else if (page is Shell shell)
        {
            shell.Navigated += (_, _) => { Detach(); TryAttach(shell.CurrentPage); };
            TryAttach(shell.CurrentPage);
        }
        else if (page is NavigationPage nav)
        {
            nav.Pushed += (_, _) => { Detach(); TryAttach(nav.CurrentPage); };
            nav.Popped += (_, _) => { Detach(); TryAttach(nav.CurrentPage); };
            TryAttach(nav.CurrentPage);
        }
    }

    void Detach()
    {
        if (_hostedPage is ContentPage cp && cp.Content is Grid g && g.StyleId == OverlayHostStyleId)
        {
            // Leave the wrapper grid in place; tearing it down on every
            // navigation would cause layout flicker. We just clear the
            // overlay reference; the new TryAttach picks up the existing
            // grid by StyleId.
        }
        _overlay = null;
        _hostedPage = null;
    }

    const string OverlayHostStyleId = "__hotreload_overlay_host__";
}
