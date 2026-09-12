using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace SinAIPrompt;

// One WebView owns one fullscreen transition. The shell supplies expansion only.
internal sealed class EditorFullscreen
{
    readonly Func<Window> owner;
    readonly Action<bool> expand;
    WindowState state;
    WindowStyle style;
    ResizeMode resize;
    Rect bounds;
    bool active;

    internal EditorFullscreen(CoreWebView2 browser, Func<Window> owner, Action<bool> expand)
    {
        this.owner = owner; this.expand = expand;
        browser.ContainsFullScreenElementChanged += (_, _) => Set(browser.ContainsFullScreenElement);
    }

    void Set(bool fullscreen)
    {
        if (active == fullscreen) return;
        var window = owner(); active = fullscreen;
        if (fullscreen)
        {
            state = window.WindowState; style = window.WindowStyle; resize = window.ResizeMode;
            bounds = state == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
            var monitor = ScreenCapture.MonitorBounds(new("", new WindowInteropHelper(window).Handle, default));
            expand(true);
            window.WindowState = WindowState.Normal; window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
            ScreenCapture.Place(window, monitor);
        }
        else
        {
            window.WindowStyle = style; window.ResizeMode = resize;
            window.Left = bounds.Left; window.Top = bounds.Top; window.Width = bounds.Width; window.Height = bounds.Height;
            window.WindowState = state; expand(false);
        }
    }
}
