using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace AgentHarness;

// PROTOTYPE: TerminalPane hosts a WebView2 running xterm.js, bridged to a ConPtySession.
// Data flow:
//   ConPTY stdout → OutputReceived event → PostWebMessageAsString → xterm.js write
//   xterm.js keypress → chrome.webview.postMessage → WebMessageReceived → ConPTY stdin

public partial class TerminalPane : UserControl
{
    private ConPtySession? _session;
    private bool _webViewReady;
    private readonly Queue<string> _pendingMessages = new();

    public string AgentName
    {
        get => AgentLabel.Text;
        set => AgentLabel.Text = value;
    }

    public TerminalPane()
    {
        InitializeComponent();
    }

    public async Task InitAsync(string command, string agentName, string initText)
    {
        AgentName = agentName;

        await WebView.EnsureCoreWebView2Async();
        WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        // Navigate to local terminal.html
        string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "terminal.html");
        WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        WebView.Source = new Uri(htmlPath);

        // Start the child process
        _session = new ConPtySession();
        _session.OutputReceived += OnConPtyOutput;
        _session.Start(command);

        // Allow shell to initialize before sending init text
        await Task.Delay(500);
        _session.Write(initText + "\r\n");
    }

    public void SendText(string text) => _session?.Write(text + "\r\n");

    // ConPTY → xterm.js: serialize output as JSON and post to WebView
    private void OnConPtyOutput(string text)
    {
        var json = JsonSerializer.Serialize(new { type = "output", data = text });
        Dispatcher.Invoke(() =>
        {
            if (_webViewReady)
                WebView.CoreWebView2.PostWebMessageAsString(json);
            else
                _pendingMessages.Enqueue(json);
        });
    }

    // xterm.js → ConPTY: forward raw input from the JS terminal
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        _session?.Write(e.TryGetWebMessageAsString() ?? "");
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _webViewReady = true;
        while (_pendingMessages.TryDequeue(out var msg))
            WebView.CoreWebView2.PostWebMessageAsString(msg);
    }
}
