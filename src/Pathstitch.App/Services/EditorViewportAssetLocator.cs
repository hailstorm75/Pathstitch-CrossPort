using System;
using System.IO;
using Domain.App.Services;

namespace Pathstitch.App.Services;

public sealed class EditorViewportAssetLocator : IEditorViewportAssetLocator
{
    private const string ThreeJsCdnUrl = "https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js";
    private const string OrbitControlsCdnUrl = "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/controls/OrbitControls.js";
    private const string TransformControlsCdnUrl = "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/controls/TransformControls.js";
    private const string BridgeShim = """
        <script>
        (function () {
            function postToHost(message) {
                if (typeof invokeCSharpAction === 'function') {
                    invokeCSharpAction(message);
                    return;
                }

                if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
                    window.chrome.webview.postMessage(message);
                    return;
                }

                if (window.external && typeof window.external.sendMessage === 'function') {
                    window.external.sendMessage(message);
                    return;
                }

                console.warn('Pathstitch host bridge unavailable', message);
            }

            if (!window.webkit) window.webkit = {};
            if (!window.webkit.messageHandlers) window.webkit.messageHandlers = {};
            window.webkit.messageHandlers.pathstitch = { postMessage: postToHost };
        })();
        </script>
        """;

    public string GetViewportHtml()
    {
        var viewportPath = GetViewportPath();
        var html = File.ReadAllText(viewportPath);
        html = RewriteRemoteViewportScripts(html);
        html = InlineVendoredViewportScripts(html, Path.GetDirectoryName(viewportPath)!);
        return html.Contains("</head>", StringComparison.OrdinalIgnoreCase)
            ? html.Replace("</head>", $"{BridgeShim}{Environment.NewLine}</head>", StringComparison.OrdinalIgnoreCase)
            : $"{BridgeShim}{Environment.NewLine}{html}";
    }

    public Uri GetViewportBaseUri()
    {
        var viewportPath = GetViewportPath();
        var baseDirectory = Path.GetDirectoryName(viewportPath)!;
        return new Uri(baseDirectory + Path.DirectorySeparatorChar, UriKind.Absolute);
    }

    private static string GetViewportPath()
        => Path.Combine(AppContext.BaseDirectory, "Assets", "Web", "viewport3d.html");

    private static string RewriteRemoteViewportScripts(string html)
    {
        return html
            .Replace(ThreeJsCdnUrl, "vendor/three.min.js", StringComparison.Ordinal)
            .Replace(OrbitControlsCdnUrl, "vendor/OrbitControls.js", StringComparison.Ordinal)
            .Replace(TransformControlsCdnUrl, "vendor/TransformControls.js", StringComparison.Ordinal);
    }

    private static string InlineVendoredViewportScripts(string html, string webAssetDirectory)
    {
        html = InlineVendoredScript(html, webAssetDirectory, "vendor/three.min.js");
        html = InlineVendoredScript(html, webAssetDirectory, "vendor/OrbitControls.js");
        return InlineVendoredScript(html, webAssetDirectory, "vendor/TransformControls.js");
    }

    private static string InlineVendoredScript(string html, string webAssetDirectory, string relativePath)
    {
        var scriptTag = $"<script src=\"{relativePath}\"></script>";
        if (!html.Contains(scriptTag, StringComparison.Ordinal))
            throw new InvalidDataException($"Viewport script tag was not found: {scriptTag}");

        var scriptPath = Path.Combine(webAssetDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var script = File.ReadAllText(scriptPath)
            .Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);

        return html.Replace(
            scriptTag,
            $"<script>{Environment.NewLine}{script}{Environment.NewLine}</script>",
            StringComparison.Ordinal);
    }
}
