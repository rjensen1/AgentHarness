using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentHarness;

// PROTOTYPE: ConPtySession wraps the Windows Pseudo Console (ConPTY) API via P/Invoke.
// Reference: microsoft/terminal EchoCon sample for P/Invoke signatures.
// One session = one child process connected to a ConPTY.

public sealed class ConPtySession : IDisposable
{
    private IntPtr _hPC      = IntPtr.Zero;  // pseudo console handle
    private IntPtr _hPipeIn  = IntPtr.Zero;  // write end → ConPTY stdin
    private IntPtr _hPipeOut = IntPtr.Zero;  // read end  ← ConPTY stdout
    private IntPtr _hProcess = IntPtr.Zero;
    private IntPtr _hThread  = IntPtr.Zero;
    private Thread? _readThread;
    private volatile bool _disposed;

    public event Action<string>? OutputReceived;

    // extraEnv: key/value pairs merged on top of the inherited environment.
    // Use to override TERM, COLORTERM, or inject any missing vars.
    public void Start(string command, short cols = 220, short rows = 50,
                      Dictionary<string, string>? extraEnv = null)
    {
        if (!NativeMethods.CreatePipe(out var hPipeInRead,  out var hPipeInWrite,  IntPtr.Zero, 0))
            throw new InvalidOperationException($"CreatePipe (in) failed: {Marshal.GetLastWin32Error()}");
        if (!NativeMethods.CreatePipe(out var hPipeOutRead, out var hPipeOutWrite, IntPtr.Zero, 0))
            throw new InvalidOperationException($"CreatePipe (out) failed: {Marshal.GetLastWin32Error()}");

        _hPipeIn  = hPipeInWrite;
        _hPipeOut = hPipeOutRead;

        var size = new COORD { X = cols, Y = rows };
        int hr = NativeMethods.CreatePseudoConsole(size, hPipeInRead, hPipeOutWrite, 0, out _hPC);
        if (hr != 0) throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{hr:X8}");

        NativeMethods.CloseHandle(hPipeInRead);
        NativeMethods.CloseHandle(hPipeOutWrite);

        // Build Unicode environment block merging parent env + terminal defaults + extras
        var envBlock = BuildEnvBlock(extraEnv);

        IntPtr attrList    = IntPtr.Zero;
        IntPtr hPCValuePtr = IntPtr.Zero;
        try
        {
            IntPtr attrListSize = IntPtr.Zero;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
            attrList = Marshal.AllocHGlobal(attrListSize);
            if (!NativeMethods.InitializeProcThreadAttributeList(attrList, 1, 0, ref attrListSize))
                throw new InvalidOperationException($"InitializeProcThreadAttributeList failed: {Marshal.GetLastWin32Error()}");

            hPCValuePtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(hPCValuePtr, _hPC);
            if (!NativeMethods.UpdateProcThreadAttribute(attrList, 0,
                    (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    hPCValuePtr, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException($"UpdateProcThreadAttribute failed: {Marshal.GetLastWin32Error()}");

            var si = new STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            si.lpAttributeList = attrList;

            // CREATE_UNICODE_ENVIRONMENT tells CreateProcess the env block is UTF-16
            uint flags = EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT;

            if (!NativeMethods.CreateProcess(null, command,
                    IntPtr.Zero, IntPtr.Zero, false,
                    flags, envBlock, null,
                    ref si, out var pi))
                throw new InvalidOperationException($"CreateProcess failed: {Marshal.GetLastWin32Error()}");

            _hProcess = pi.hProcess;
            _hThread  = pi.hThread;
        }
        finally
        {
            if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
            if (attrList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attrList);
                Marshal.FreeHGlobal(attrList);
            }
            if (hPCValuePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(hPCValuePtr);
        }

        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = $"ConPty-{command[..Math.Min(8, command.Length)]}"
        };
        _readThread.Start();
    }

    public void Write(string text)
    {
        if (_disposed || _hPipeIn == IntPtr.Zero) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        NativeMethods.WriteFile(_hPipeIn, bytes, (uint)bytes.Length, out _, IntPtr.Zero);
    }

    private void ReadLoop()
    {
        var buf = new byte[8192];
        while (!_disposed)
        {
            if (!NativeMethods.ReadFile(_hPipeOut, buf, (uint)buf.Length, out uint bytesRead, IntPtr.Zero) || bytesRead == 0)
                break;
            OutputReceived?.Invoke(Encoding.UTF8.GetString(buf, 0, (int)bytesRead));
        }
    }

    // Builds a Windows Unicode environment block from the current process env,
    // overriding with terminal capability vars and any caller-supplied extras.
    // Format: KEY=VALUE\0KEY=VALUE\0\0 (UTF-16LE, double-null terminated)
    private static IntPtr BuildEnvBlock(Dictionary<string, string>? extraEnv)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Inherit parent process environment
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            env[(string)entry.Key] = (string?)entry.Value ?? "";

        // Advertise proper terminal capabilities so child processes render correctly
        env["TERM"]      = "xterm-256color";
        env["COLORTERM"] = "truecolor";

        // Caller overrides (e.g. ANTHROPIC_API_KEY if not in user env)
        if (extraEnv != null)
            foreach (var kv in extraEnv)
                env[kv.Key] = kv.Value;

        var sb = new StringBuilder();
        foreach (var kv in env)
        {
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value);
            sb.Append('\0');
        }
        sb.Append('\0');  // double-null terminator

        var bytes = Encoding.Unicode.GetBytes(sb.ToString());
        var ptr   = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hPC      != IntPtr.Zero) NativeMethods.ClosePseudoConsole(_hPC);
        if (_hPipeIn  != IntPtr.Zero) NativeMethods.CloseHandle(_hPipeIn);
        if (_hPipeOut != IntPtr.Zero) NativeMethods.CloseHandle(_hPipeOut);
        if (_hProcess != IntPtr.Zero) NativeMethods.CloseHandle(_hProcess);
        if (_hThread  != IntPtr.Zero) NativeMethods.CloseHandle(_hThread);
    }

    // --- Constants ---
    private const uint EXTENDED_STARTUPINFO_PRESENT       = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT         = 0x00000400;
    private const int  PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    // --- Structs ---
    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int      cb;
        public string?  lpReserved, lpDesktop, lpTitle;
        public int      dwX, dwY, dwXSize, dwYSize;
        public int      dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short    wShowWindow, cbReserved2;
        public IntPtr   lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr      lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int    dwProcessId, dwThreadId;
    }

    // --- P/Invoke ---
    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput,
            uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe,
            IntPtr lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr hFile, [Out] byte[] lpBuffer,
            uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer,
            uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcess(string? lpApplicationName, string lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
            uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
            [In] ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList,
            int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags,
            IntPtr Attribute, IntPtr lpValue, IntPtr cbSize,
            IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeleteProcThreadAttributeList(IntPtr lpAttributeList);
    }
}
