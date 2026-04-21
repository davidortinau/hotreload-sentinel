namespace HotReloadSentinel.Diagnostics.Maui;

using System.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

/// <summary>
/// Small status pill shown in the lower-left of the active window.
/// Green: most recent apply succeeded. Auto-fades after ~2s.
/// Red: most recent apply failed (or CLI reported a stuck apply). Sticky
/// until the next successful apply.
/// </summary>
public sealed class HotReloadOverlay : Border
{
    static readonly Color GreenBg = Color.FromArgb("#1F8B45");
    static readonly Color RedBg = Color.FromArgb("#C03A3A");
    static readonly TimeSpan FadeDelay = TimeSpan.FromSeconds(2.5);

    readonly HotReloadOverlayState _state;
    readonly Label _label;

    public HotReloadOverlay(HotReloadOverlayState state)
    {
        _state = state;

        Padding = new Thickness(10, 4);
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 12 };
        BackgroundColor = Colors.Transparent;
        IsVisible = false;
        Opacity = 0;
        InputTransparent = true;
        ZIndex = int.MaxValue;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.End;
        Margin = new Thickness(12, 0, 0, 24);

        _label = new Label
        {
            FontSize = 12,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
        };
        Content = _label;

        _state.PropertyChanged += OnStateChanged;
        Render();
    }

    void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.IsDispatchRequired)
            dispatcher?.Dispatch(Render);
        else
            Render();
    }

    void Render()
    {
        switch (_state.Status)
        {
            case HotReloadOverlayState.OverlayStatus.Applied:
                BackgroundColor = GreenBg;
                _label.Text = $"HR ✓  #{_state.AppliedCount}";
                ShowAndScheduleFade(_state.ObservedSequence);
                break;

            case HotReloadOverlayState.OverlayStatus.Failed:
                BackgroundColor = RedBg;
                var reason = string.IsNullOrWhiteSpace(_state.FailureReason)
                    ? "no app-side tick"
                    : _state.FailureReason!;
                _label.Text = $"HR ✗  {reason}";
                CancelFade();
                IsVisible = true;
                Opacity = 1;
                break;

            default:
                CancelFade();
                IsVisible = false;
                Opacity = 0;
                break;
        }
    }

    CancellationTokenSource? _fadeCts;

    void ShowAndScheduleFade(long sequence)
    {
        CancelFade();
        IsVisible = true;
        Opacity = 1;
        _fadeCts = new CancellationTokenSource();
        var token = _fadeCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FadeDelay, token);
            }
            catch (TaskCanceledException) { return; }
            if (token.IsCancellationRequested) return;

            Dispatcher?.Dispatch(async () =>
            {
                if (_state.Status != HotReloadOverlayState.OverlayStatus.Applied) return;
                if (sequence != _state.ObservedSequence) return;
                try { await this.FadeToAsync(0, 250); } catch { }
                _state.DismissApplied(sequence);
            });
        });
    }

    void CancelFade()
    {
        try { _fadeCts?.Cancel(); } catch { }
        _fadeCts = null;
    }
}
