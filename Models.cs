using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseMacro;

public enum MouseButtonKind
{
    Left,
    Right,
}

public class Step
{
    public int X { get; set; }
    public int Y { get; set; }
    public MouseButtonKind Button { get; set; } = MouseButtonKind.Left;
    public int DelayMs { get; set; } = 500; // delay AFTER this step
    public string Note { get; set; } = "";
}

public class MacroConfig
{
    public List<Step> Steps { get; set; } = DefaultSteps();
    public int Repeat { get; set; } = 1; // 0 = infinite
    public int StartDelaySec { get; set; } = 3;
    public int JitterMs { get; set; } = 0; // random +/- jitter applied to each delay
    public string Monitor { get; set; } = ""; // Screen.DeviceName; empty = primary
    public bool RelativeToMonitor { get; set; } = true;
    public bool ScaleToResolution { get; set; } = true;
    public int RefWidth { get; set; } = 2560;
    public int RefHeight { get; set; } = 1440;

    public static List<Step> DefaultSteps() => new()
    {
        new Step { X = 176, Y = 758, DelayMs = 2000, Note = "도면 사용 + 칸" },
        new Step { X = 702, Y = 1105, DelayMs = 0, Note = "왼쪽에서 3번째 종이" },
    };

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseMacro", "config.json");

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public static MacroConfig Load(string path) =>
        JsonSerializer.Deserialize<MacroConfig>(File.ReadAllText(path), Options) ?? new MacroConfig();

    // Reference-resolution coordinates -> absolute screen pixels.
    public Point ToAbsolute(int x, int y, Rectangle monitor)
    {
        if (!RelativeToMonitor) return new Point(x, y);
        double fx = 1, fy = 1;
        if (ScaleToResolution && RefWidth > 0 && RefHeight > 0)
        {
            fx = (double)monitor.Width / RefWidth;
            fy = (double)monitor.Height / RefHeight;
        }
        return new Point(monitor.X + (int)Math.Round(x * fx), monitor.Y + (int)Math.Round(y * fy));
    }

    // Absolute screen pixels -> reference-resolution coordinates (inverse of ToAbsolute).
    public Point FromAbsolute(Point abs, Rectangle monitor)
    {
        if (!RelativeToMonitor) return abs;
        double fx = 1, fy = 1;
        if (ScaleToResolution && RefWidth > 0 && RefHeight > 0)
        {
            fx = (double)RefWidth / monitor.Width;
            fy = (double)RefHeight / monitor.Height;
        }
        return new Point((int)Math.Round((abs.X - monitor.X) * fx), (int)Math.Round((abs.Y - monitor.Y) * fy));
    }
}
