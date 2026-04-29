namespace AgentHarness;

// PROTOTYPE: InputRouter routes text from the bottom input panel to a specific TerminalPane.

public class InputRouter
{
    private readonly TerminalPane[] _panes;

    public InputRouter(params TerminalPane[] panes) => _panes = panes;

    public void Route(int agentIndex, string text)
    {
        if (agentIndex >= 0 && agentIndex < _panes.Length)
            _panes[agentIndex].SendText(text);
    }
}
