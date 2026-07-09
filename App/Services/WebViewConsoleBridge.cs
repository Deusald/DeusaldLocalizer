using System.Text.Json;
using Microsoft.AspNetCore.Components.WebView.Maui;
#if MACCATALYST || IOS
using Foundation;
using WebKit;
#endif

namespace App;

/// Forwards the Blazor WebView's own console output — JS `console.*`, uncaught
/// script exceptions, and (on Windows) resource/network load failures — into the
/// same file log as the .NET side, so `AppLog.LogFilePath` is the single place to
/// look. Without this, anything that happens inside the WebView is only visible in
/// the browser dev-tools console, never on disc.
public static class WebViewConsoleBridge
{
    /// Hook the given BlazorWebView. Call once, right after InitializeComponent().
    public static void Attach(BlazorWebView webView)
    {
        // Subscribe with a lambda so the platform-specific event-args type is never
        // named here; e.WebView is already the concrete platform web view.
        webView.BlazorWebViewInitialized += (_, e) => OnInitialized(e.WebView);
    }

    #if WINDOWS
    private static async void OnInitialized(Microsoft.UI.Xaml.Controls.WebView2 webView)
    {
        try
        {
            var core = webView.CoreWebView2;

            // console.log / warn / error / info / debug
            core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived += (_, ev) =>
                LogConsoleApiCalled(ev.ParameterObjectAsJson);

            // uncaught JS exceptions (e.g. "Blazor has already started.")
            core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, ev) =>
                LogExceptionThrown(ev.ParameterObjectAsJson);

            // resource load / network / CSP errors (e.g. favicon ERR_ADDRESS_UNREACHABLE)
            core.GetDevToolsProtocolEventReceiver("Log.entryAdded").DevToolsProtocolEventReceived += (_, ev) =>
                LogEntryAdded(ev.ParameterObjectAsJson);

            await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
            await core.CallDevToolsProtocolMethodAsync("Log.enable", "{}");
        }
        catch (Exception ex)
        {
            AppLog.LogFatal("WebViewConsoleBridge.Init", ex);
        }
    }

    private static void LogConsoleApiCalled(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "log" : "log";

            var parts = new List<string>();
            if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
            {
                foreach (var arg in args.EnumerateArray())
                    parts.Add(DescribeRemoteObject(arg));
            }
            AppLog.Write(LevelForConsoleType(type), "WebView.console", string.Join(" ", parts));
        }
        catch (Exception ex) { AppLog.LogFatal("WebViewConsoleBridge.Console", ex); }
    }

    private static void LogExceptionThrown(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            string message = "<unknown JS exception>";
            if (doc.RootElement.TryGetProperty("exceptionDetails", out var details))
            {
                if (details.TryGetProperty("exception", out var ex) && ex.TryGetProperty("description", out var desc))
                    message = desc.GetString() ?? message;
                else if (details.TryGetProperty("text", out var text))
                    message = text.GetString() ?? message;
            }
            AppLog.Write("ERROR", "WebView.exception", message);
        }
        catch (Exception ex) { AppLog.LogFatal("WebViewConsoleBridge.Exception", ex); }
    }

    private static void LogEntryAdded(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("entry", out var entry)) return;

            string level  = entry.TryGetProperty("level",  out var l) ? l.GetString() ?? "info" : "info";
            string source = entry.TryGetProperty("source", out var s) ? s.GetString() ?? "log"  : "log";
            string text   = entry.TryGetProperty("text",   out var x) ? x.GetString() ?? ""     : "";
            if (entry.TryGetProperty("url", out var u) && u.GetString() is { Length: > 0 } url)
                text += $" ({url})";

            AppLog.Write(LevelForConsoleType(level), $"WebView.{source}", text);
        }
        catch (Exception ex) { AppLog.LogFatal("WebViewConsoleBridge.LogEntry", ex); }
    }

    private static string DescribeRemoteObject(JsonElement arg)
    {
        if (arg.TryGetProperty("value", out var value)) return value.ToString();
        if (arg.TryGetProperty("description", out var desc)) return desc.GetString() ?? "";
        return arg.TryGetProperty("type", out var t) ? $"<{t.GetString()}>" : "<?>";
    }
    #elif MACCATALYST || IOS
    // WKWebView has no DevTools protocol reachable from native, so inject a shim that
    // mirrors console output and uncaught errors over a script message handler.
    private const string _INJECTED_JS =
        "(function(){if(window.__appLogHooked)return;window.__appLogHooked=true;" +
        "function send(lvl,a){try{window.webkit.messageHandlers.appLog.postMessage(" +
        "JSON.stringify({level:lvl,text:Array.prototype.map.call(a,String).join(' ')}));}catch(e){}}" +
        "['log','info','warn','error','debug'].forEach(function(m){var o=console[m]?console[m].bind(console):function(){};" +
        "console[m]=function(){send(m,arguments);o.apply(console,arguments);};});" +
        "window.addEventListener('error',function(e){send('error',[e.message+' @ '+(e.filename||'')+':'+(e.lineno||'')]);});" +
        "window.addEventListener('unhandledrejection',function(e){send('error',['UnhandledRejection: '+((e.reason&&e.reason.stack)||e.reason)]);});" +
        "})();";

    private static void OnInitialized(WKWebView webView)
    {
        try
        {
            var controller = webView.Configuration.UserContentController;
            controller.AddScriptMessageHandler(new ConsoleScriptHandler(), "appLog");
            // Applies to future loads/reloads…
            controller.AddUserScript(new WKUserScript(new NSString(_INJECTED_JS), WKUserScriptInjectionTime.AtDocumentStart, true));
            // …and inject once now for the already-loaded page.
            webView.EvaluateJavaScript(_INJECTED_JS, (_, _) => { });
        }
        catch (Exception ex)
        {
            AppLog.LogFatal("WebViewConsoleBridge.Init", ex);
        }
    }

    private sealed class ConsoleScriptHandler : NSObject, IWKScriptMessageHandler
    {
        public void DidReceiveScriptMessage(WKUserContentController controller, WKScriptMessage message)
        {
            try
            {
                using var doc = JsonDocument.Parse(message.Body?.ToString() ?? "{}");
                var root = doc.RootElement;
                string level = root.TryGetProperty("level", out var l) ? l.GetString() ?? "log" : "log";
                string text  = root.TryGetProperty("text",  out var t) ? t.GetString() ?? ""    : "";
                AppLog.Write(LevelForConsoleType(level), "WebView.console", text);
            }
            catch (Exception ex) { AppLog.LogFatal("WebViewConsoleBridge.Console", ex); }
        }
    }
    #else
    private static void OnInitialized(object webView) { }
    #endif

    private static string LevelForConsoleType(string type) => type switch
    {
        "error"                       => "ERROR",
        "warn" or "warning"           => "WARN",
        "debug" or "verbose" or "trace" => "DEBUG",
        _                             => "INFO",
    };
}
