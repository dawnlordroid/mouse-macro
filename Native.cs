using System.Runtime.InteropServices;

namespace MouseMacro;

static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const int HOTKEY_STOP = 1;
    public const int HOTKEY_CAPTURE = 2;
    public const int HOTKEY_TOGGLE = 3;
    public const uint VK_F8 = 0x77;
    public const uint VK_F10 = 0x79;
    public const uint VK_F12 = 0x7B;

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }

    public static Point GetCursor()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int WM_MBUTTONDOWN = 0x0207;

    public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);

    public static bool IsKeyDown(uint vk) => (GetAsyncKeyState((int)vk) & 0x8000) != 0;

    static int held; // 0 = none, 1 = left down, 2 = right down

    static INPUT MouseEvent(uint flag) => new() { type = 0, mi = new MOUSEINPUT { dwFlags = flag } };

    // Always finishes with a mouse-up once the mouse-down was sent, even when cancelled.
    public static void Click(int x, int y, MouseButtonKind button, int moveWaitMs, int pressMs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SetCursorPos(x, y);
        if (moveWaitMs > 0 && ct.WaitHandle.WaitOne(moveWaitMs)) throw new OperationCanceledException(ct);
        bool right = button == MouseButtonKind.Right;
        var down = MouseEvent(right ? 0x0008u : 0x0002u);
        var up = MouseEvent(right ? 0x0010u : 0x0004u);
        held = right ? 2 : 1;
        SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>());
        try
        {
            if (pressMs > 0) ct.WaitHandle.WaitOne(pressMs);
        }
        finally
        {
            SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>());
            held = 0;
        }
    }

    public static void ReleaseHeldButton()
    {
        int h = Interlocked.Exchange(ref held, 0);
        if (h == 0) return;
        SendInput(1, new[] { MouseEvent(h == 2 ? 0x0010u : 0x0004u) }, Marshal.SizeOf<INPUT>());
    }
}
