using System;
using Avalonia.Controls;

namespace Pathstitch.App.Pages;

/// <summary>
/// Keeps the native WebView boundary replaceable for display-server-free UI tests.
/// Production always creates the real WebView; tests suppress only that native child.
/// </summary>
public sealed class EditorViewportWebView : ContentControl
{
    private readonly NativeWebView? _nativeWebView;

    internal static bool DisableNativeChildForAutomationTests { get; set; }

    public EditorViewportWebView()
    {
        if (DisableNativeChildForAutomationTests)
            return;

        _nativeWebView = new NativeWebView();
        _nativeWebView.NavigationCompleted += (_, args) => NavigationCompleted?.Invoke(this, args);
        _nativeWebView.WebMessageReceived += (_, args) => WebMessageReceived?.Invoke(this, args);
        Content = _nativeWebView;
    }

    public event EventHandler<WebViewNavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;

    public void NavigateToString(string html, Uri baseUri)
        => _nativeWebView?.NavigateToString(html, baseUri);

    public void InvokeScript(string script)
        => _nativeWebView?.InvokeScript(script);
}
