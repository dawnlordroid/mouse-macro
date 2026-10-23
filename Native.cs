using System.Runtime.InteropServices;

namespace MouseMacro;

static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const int HOTKEY_ID = 1;
    public const uint VK_F12 = 0x7B;

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(30);
        var down = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0002 } };
        var up = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0004 } };
        SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>());
        Thread.Sleep(40);
        SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>());
    }

    // Captures the whole virtual screen (all monitors).
    public static Bitmap CaptureScreen(out Rectangle bounds)
    {
        bounds = SystemInformation.VirtualScreen;
        var bmp = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bmp;
    }
}
