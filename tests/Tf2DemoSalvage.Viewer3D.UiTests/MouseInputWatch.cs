using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// Counts the mouse input the desktop receives while it is alive, split by whether Windows marks it injected — synthesized
/// by a program through <c>SendInput</c> or <c>mouse_event</c> — or not, which is a hand on the mouse.
/// </summary>
/// <remarks>
/// **Why a test needs the split** (owner, 2026-11-12: *"it normally doesnt matter, idk why its intermittnet"*): a test that
/// claims a helper drives the viewer without real input used to compare the cursor before and after, and any hand on the
/// mouse in that window failed it. A <c>WH_MOUSE_LL</c> hook sees every mouse event before it reaches a window, with
/// <c>LLMHF_INJECTED</c> set on the synthesized ones, so a helper that clicks is caught whatever the hand does.
///
/// **What it cannot see:** <c>SetCursorPos</c>, which moves the cursor without an input event. A caller therefore still
/// compares the cursor, but only when <see cref="Human"/> is zero — the stretch in which nothing else could have moved it.
///
/// The hook runs on a thread of its own that pumps messages, as a low-level hook requires; the system calls it there.
/// </remarks>
internal sealed partial class MouseInputWatch : IDisposable
{
    private const int WhMouseLowLevel = 14;
    private const uint WmQuit = 0x0012;

    /// <summary><c>LLMHF_INJECTED</c>; <c>LLMHF_LOWER_IL_INJECTED</c> (0x2) is only ever set beside it.</summary>
    private const int InjectedFlag = 0x1;

    /// <summary>The offset of <c>flags</c> in <c>MSLLHOOKSTRUCT</c>: after <c>POINT pt</c> and <c>DWORD mouseData</c>.</summary>
    private const int FlagsOffset = 12;

    private static readonly TimeSpan StartLimit = TimeSpan.FromSeconds(10);

    private readonly HookProcedure _procedure;
    private readonly Thread _pump;
    private readonly ManualResetEventSlim _ready = new();
    private uint _pumpThreadId;
    private int _hookError;
    private int _injected;
    private int _human;

    public MouseInputWatch()
    {
        // Held in a field for the hook's whole life: the system calls through a pointer to it.
        _procedure = Observe;
        _pump = new Thread(Pump) { IsBackground = true, Name = "MouseInputWatch" };
        _pump.Start();

        if (!_ready.Wait(StartLimit))
        {
            throw new InvalidOperationException("the mouse hook thread did not start");
        }

        if (_hookError != 0)
        {
            _pump.Join();
            throw new InvalidOperationException($"SetWindowsHookEx(WH_MOUSE_LL) failed: error {_hookError}");
        }
    }

    private delegate nint HookProcedure(int code, nint message, nint data);

    /// <summary>Mouse events a program synthesized: any at all means something drove the mouse.</summary>
    public int Injected => Volatile.Read(ref _injected);

    /// <summary>Mouse events from a device: a hand on the mouse.</summary>
    public int Human => Volatile.Read(ref _human);

    /// <summary>
    /// Sends one relative mouse move through <c>SendInput</c> — injected input, as a synthesized click is, which
    /// FlaUI's <c>Mouse.MoveBy</c> is not (it sets the cursor with <c>SetCursorPos</c>, measured 2026-11-12: two moves
    /// counted nothing).
    /// </summary>
    /// <exception cref="InvalidOperationException">The system refused the input.</exception>
    public static void SendMove(int dx, int dy)
    {
        Input[] input = [new Input { Type = InputMouse, Mouse = new MouseInput { X = dx, Y = dy, Flags = MouseEventMove } }];

        if (SendInput(1, input, Marshal.SizeOf<Input>()) != 1)
        {
            throw new InvalidOperationException($"SendInput failed: error {Marshal.GetLastPInvokeError()}");
        }
    }

    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    /// <summary><c>INPUT</c>: the type, then the union, whose mouse member is the largest the mouse needs.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    public void Dispose()
    {
        PostThreadMessage(_pumpThreadId, WmQuit, 0, 0);
        _pump.Join();
        _ready.Dispose();
    }

    private void Pump()
    {
        _pumpThreadId = GetCurrentThreadId();

        nint hook = SetWindowsHookEx(
            WhMouseLowLevel, Marshal.GetFunctionPointerForDelegate(_procedure), GetModuleHandle(null), 0);

        if (hook == 0)
        {
            _hookError = Marshal.GetLastPInvokeError();
            _ready.Set();
            return;
        }

        _ready.Set();

        try
        {
            while (GetMessage(out Message _, 0, 0, 0) > 0)
            {
                // Nothing to dispatch: the pump exists so the system can call the hook on this thread.
            }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
        }
    }

    private nint Observe(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            if ((Marshal.ReadInt32(data, FlagsOffset) & InjectedFlag) != 0)
            {
                Interlocked.Increment(ref _injected);
            }
            else
            {
                Interlocked.Increment(ref _human);
            }
        }

        return CallNextHookEx(0, code, message, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetWindowsHookEx(int hook, nint procedure, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetMessage(out Message message, nint window, uint first, uint last);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetModuleHandle(string? module);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetCurrentThreadId();
}
