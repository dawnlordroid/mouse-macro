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

    public static void Click(int x, int y, MouseButtonKind button)
    {
        SetCursorPos(x, y);
        Thread.Sleep(30);
        uint downFlag = button == MouseButtonKind.Right ? 0x0008u : 0x0002u;
        uint upFlag = button == MouseButtonKind.Right ? 0x0010u : 0x0004u;
        var down = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = downFlag } };
        var up = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = upFlag } };
        SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>());
        Thread.Sleep(40);
        SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>());
    }
}
