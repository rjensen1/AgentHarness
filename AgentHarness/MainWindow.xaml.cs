using System.Windows;
using System.Windows.Input;

namespace AgentHarness;

// PROTOTYPE: MainWindow hosts 3 TerminalPanes and an input panel.
// On startup each pane spawns a ConPtySession running cmd.exe (or claude).

public partial class MainWindow : Window
{
    private InputRouter? _router;

    // PROTOTYPE: Customize these to run Claude Code instead of cmd.exe once tested
    private static readonly (string Command, string AgentName, string InitText)[] AgentConfig =
    [
        ("cmd.exe", "Agent 1", "echo Agent 1 ready"),
        ("cmd.exe", "Agent 2", "echo Agent 2 ready"),
        ("cmd.exe", "Agent 3", "echo Agent 3 ready"),
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
