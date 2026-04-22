namespace HotReloadSentinel.Diagnostics.Maui;

using System.ComponentModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

/// <summary>
/// Native window-level overlay rendering a status pill in the lower-left of
/// the window. Draws on top of the page tree via <see cref="IWindowOverlay"/>
/// so it survives re-renders from MVU frameworks (MauiReactor, MVVM with
/// view swaps) that would otherwise replace page content and wipe a
/// page-tree-attached overlay.
/// </summary>
internal sealed class HotReloadWindowOverlay : WindowOverlay
{
    readonly HotReloadOverlayState _state;
    readonly PillElement _pill;
    CancellationTokenSource? _fadeCts;

    public HotReloadWindowOverlay(IWindow window, HotReloadOverlayState state) : base(window)
    {
        _state = state;
        _pill = new PillElement(_state);
        AddWindowElement(_pill);
        // Pill is informational only — let touches pass through to the app.
        DisableUITouchEventPassthrough = false;

        _state.PropertyChanged += OnStateChanged;
        // Initial render: probably Idle (invisible), but call once so transient
        // state at startup (e.g. a burst that fired before overlay attached)
        // still renders.
        InvalidateOnDispatcher();
        MaybeScheduleFade();
    }

    void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateOnDispatcher();
        MaybeScheduleFade();
    }

    void InvalidateOnDispatcher()
    {
        var app = Microsoft.Maui.Controls.Application.Current;
        var dispatcher = app?.Dispatcher;
        if (dispatcher is null || !dispatcher.IsDispatchRequired)
            Invalidate();
        else
            dispatcher.Dispatch(Invalidate);
    }

    void MaybeScheduleFade()
    {
        // Only Applied status auto-dismisses; Failed stays sticky until next Applied.
        if (_state.Status != HotReloadOverlayState.OverlayStatus.Applied)
            return;

        var observedSeq = _state.ObservedSequence;

        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _fadeCts, cts);
        if (old is not null)
        {
            try { old.Cancel(); } catch { }
            old.Dispose();
        }
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromMilliseconds(2500), token); }
            catch (TaskCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            if (token.IsCancellationRequested) return;

            // Only dismiss if still on the same sequence (no new apply happened
            // during the fade delay) and still in Applied state.
            if (_state.Status == HotReloadOverlayState.OverlayStatus.Applied &&
                _state.ObservedSequence == observedSeq)
            {
                _state.DismissApplied(observedSeq);
            }
        });
    }

    sealed class PillElement : IWindowOverlayElement
    {
        static readonly Color GreenBg = Color.FromArgb("#1F8B45");
        static readonly Color RedBg = Color.FromArgb("#C03A3A");
        static readonly Color IdleBg = Color.FromArgb("#CC6A1F");
        const float Margin = 16f;
        const float PaddingX = 12f;
        const float PaddingY = 6f;
        const float CornerRadius = 12f;
        const float FontSize = 13f;

        readonly HotReloadOverlayState _state;

        public PillElement(HotReloadOverlayState state) => _state = state;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var status = _state.Status;

            string text;
            Color bg;
            switch (status)
            {
                case HotReloadOverlayState.OverlayStatus.Applied:
                    text = $"🔥 HR ✓  #{_state.AppliedCount}";
                    bg = GreenBg;
                    break;
                case HotReloadOverlayState.OverlayStatus.Failed:
                    var reason = string.IsNullOrWhiteSpace(_state.FailureReason)
                        ? "no app-side tick"
                        : _state.FailureReason!;
                    text = $"🔥 HR ✗  {reason}";
                    bg = RedBg;
                    break;
                default:
                    // Idle — show a persistent flame so the developer can
                    // confirm the overlay is wired up even before any apply.
                    text = _state.AppliedCount > 0
                        ? $"🔥 HR  #{_state.AppliedCount}"
                        : "🔥 HR";
                    bg = IdleBg;
                    break;
            }

            canvas.SaveState();
            try
            {
                canvas.FontColor = Colors.White;
                canvas.FontSize = FontSize;
                canvas.Font = new Microsoft.Maui.Graphics.Font("System", 700);

                // Measure using a conservative width-per-character estimate.
                // ICanvas has no synchronous measurement API that works across
                // all backends, so we approximate. Slight over-sizing is fine.
                var approxTextWidth = text.Length * (FontSize * 0.7f);
                var pillW = approxTextWidth + PaddingX * 2;
                var pillH = FontSize + PaddingY * 2 + 4;

                var x = Margin;
                var y = dirtyRect.Height - Margin - pillH;

                canvas.FillColor = bg;
                canvas.FillRoundedRectangle(x, y, pillW, pillH, CornerRadius);

                canvas.DrawString(
                    text,
                    x,
                    y,
                    pillW,
                    pillH,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        public bool Contains(Point point) => false;
    }
}
