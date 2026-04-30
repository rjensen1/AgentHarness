using System.Windows;
using System.Windows.Input;

namespace AgentHarness;

// PROTOTYPE: MainWindow hosts 3 TerminalPanes and an input panel.
// On startup each pane spawns a ConPtySession running cmd.exe (or claude).

public partial class MainWindow : Window
{
    private InputRouter? _router;

    // PROTOTYPE: Each pane runs a separate Claude Code REPL session via ConPTY.
    // Full path used for reliability — claude.exe lives in .local/bin, not on PATH inside ConPTY.
    private static readonly (string Command, string AgentName, string InitText)[] AgentConfig =
    [
        (@"C:\Users\russj\.local\bin\claude.exe", "Agent 1", ""),
        (@"C:\Users\russj\.local\bin\claude.exe", "Agent 2", ""),
        (@"C:\Users\russj\.local\bin\claude.exe", "Agent 3", ""),
    ];

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var panes = new[] { Pane0, Pane1, Pane2 };
        _router = new InputRouter(panes);

        var tasks = AgentConfig
            .Zip(panes, (cfg, pane) => pane.InitAsync(cfg.Command, cfg.AgentName, cfg.InitText))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private void OnSendClick(object sender, RoutedEventArgs e) => SendInput();

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SendInput();
    }

    private void SendInput()
    {
        string text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        int agentIndex = AgentPicker.SelectedIndex;
        _router?.Route(agentIndex, text);
        InputBox.Clear();
        InputBox.Focus();
    }
}
