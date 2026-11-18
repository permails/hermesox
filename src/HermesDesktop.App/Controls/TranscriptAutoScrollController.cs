using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HermesDesktop.App.Controls;

/// <summary>
/// Manages sticky-bottom auto-scrolling for streaming chat transcripts without fighting user scrolling
/// or racing Avalonia layout/measure passes.
/// </summary>
public sealed class TranscriptAutoScrollController
{
    private readonly ScrollViewer _scroller;
    private int _scrollPending;
    private bool _isProgrammaticScroll;
    private bool _stickToBottom = true;

    public event Action<bool>? StickyChanged;

    public bool StickToBottom => _stickToBottom;

    public TranscriptAutoScrollController(ScrollViewer scroller)
    {
        _scroller = scroller;
        _scroller.ScrollChanged += OnScrollChanged;
        _scroller.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void RequestScrollToBottom(bool force = false)
    {
        if (force) SetSticky(true);
        if (!_stickToBottom) return;
        ScheduleScrollToEnd();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Positive Delta.Y in Avalonia scrolls upward toward older messages.
        if (e.Delta.Y > 0.05)
        {
            SetSticky(false);
        }
        else if (e.Delta.Y < -0.05 && DistanceFromBottom() <= 48)
        {
            SetSticky(true);
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isProgrammaticScroll) return;

        var layoutChanged = Math.Abs(e.ExtentDelta.Y) > 0.5 || Math.Abs(e.ViewportDelta.Y) > 0.5;
        if (layoutChanged)
        {
            if (_stickToBottom)
            {
                ScheduleScrollToEnd();
            }
            return;
        }

        if (e.OffsetDelta.Y < -0.5)
        {
            SetSticky(false);
        }
        else if (DistanceFromBottom() <= 36)
        {
            SetSticky(true);
        }
    }

    private void ScheduleScrollToEnd()
    {
        if (Interlocked.Exchange(ref _scrollPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!_stickToBottom) return;
                var maxY = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
                if (Math.Abs(_scroller.Offset.Y - maxY) > 0.5)
                {
                    _isProgrammaticScroll = true;
                    try
                    {
                        _scroller.Offset = new Vector(_scroller.Offset.X, maxY);
                    }
                    finally
                    {
                        _isProgrammaticScroll = false;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _scrollPending, 0);
            }
        }, DispatcherPriority.Render);
    }

    private double DistanceFromBottom()
    {
        var maxY = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        return Math.Max(0, maxY - _scroller.Offset.Y);
    }

    private void SetSticky(bool sticky)
    {
        if (_stickToBottom == sticky) return;
        _stickToBottom = sticky;
        StickyChanged?.Invoke(sticky);
    }
}
