# AgentHarness Step 2 Result

**Date:** 2026-04-29  
**Step:** Swap cmd.exe → claude, verify ConPTY environment

## Changes Made

### `ConPtySession.cs`
- Added `extraEnv` parameter to `Start()`
- Added `BuildEnvBlock()`: merges parent process env + injects `TERM=xterm-256color` + `COLORTERM=truecolor` + any caller-supplied extras, serialized as UTF-16LE Windows env block
- `CreateProcess` now passes `envBlock` with `CREATE_UNICODE_ENVIRONMENT` flag

### `MainWindow.xaml.cs`
- Swapped all three `AgentConfig` entries from `cmd.exe` to `C:\Users\russj\.local\bin\claude.exe`
- `InitText` set to `""` — no command sent on startup; Claude Code's REPL appears naturally

### `TerminalPane.xaml.cs`
- `InitAsync` now guards `initText` send — skips `Write()` if empty to avoid sending a bare Enter into Claude's REPL

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## ConPTY + Claude Design Notes

**Credential discovery:** `ANTHROPIC_API_KEY` is not set as a Windows user/machine env var on this machine. Claude Code uses `~/.claude/` credential store. The ConPTY child process inherits `HOME` from the parent, so credentials are accessible automatically — no key injection needed.

**TERM injection:** `TERM=xterm-256color` and `COLORTERM=truecolor` are injected via `BuildEnvBlock()`. These tell Claude Code (and any child process it spawns) that the terminal supports full color and VT sequences. Without them the ConPTY session gets `TERM=` (empty) and Claude may fall back to degraded output.

**Full path for claude.exe:** Using `C:\Users\russj\.local\bin\claude.exe` directly rather than `claude` because the ConPTY environment inherits PATH from the WPF host process, which may not include `.local\bin`. Full path is more reliable.

## Manual Verification Required

This step cannot be verified by build alone — the WPF app must be launched on the Alienware. Expected behavior:

- [ ] Three TerminalPane instances appear in the 3-column layout
- [ ] Each pane shows Claude Code's interactive prompt (e.g. `>` or the Claude banner)
- [ ] Typing a question in the input bar and selecting a pane delivers text to that Claude REPL
- [ ] Claude responds with generated text visible in the WebView2/xterm.js pane
- [ ] No crash on startup, no "claude.exe not found" error

If the interactive prompt does not appear, likely causes:
1. `claude.exe` at a different path — run `where claude` in cmd to find it
2. Claude Code prompts for login interactively (requires TTY) — REPL should auto-use `~/.claude/` credentials
3. WebView2 runtime not installed — check `about:version` in Edge

## Next Step

Step 3 (per project plan): route per-pane input through `InputRouter`, wire broadcast vs. targeted send, and verify multi-agent conversation flow.
