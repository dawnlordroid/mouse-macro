using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseMacro;

public enum ClickMode
{
    WholeCenter,
    UpperSquareCenter,
    Custom,
}

public class SequenceItem
{
    public string Path { get; set; } = "";
    public ClickMode Mode { get; set; } = ClickMode.UpperSquareCenter;
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int TimeoutMs { get; set; } = 10000;
    public bool StopIfNotFound { get; set; } = true;

    [JsonIgnore]
    public string Name => System.IO.Path.GetFileName(Path);

    public override string ToString() => Name;

    // Click point relative to the image's top-left corner.
    public Point GetClickOffset(int width, int height)
    {
        switch (Mode)
        {
            case ClickMode.WholeCenter:
                return new Point(width / 2, height / 2);
            case ClickMode.UpperSquareCenter:
                // The square slot is the upper part: a width x width square at the top.
                int side = Math.Min(width, height);
                return new Point(width / 2, side / 2);
            default:
                return new Point(Math.Clamp(OffsetX, 0, Math.Max(0, width - 1)), Math.Clamp(OffsetY, 0, Math.Max(0, height - 1)));
        }
    }
}

public class MacroConfig
{
    public List<SequenceItem> Items { get; set; } = new();
    public int MatchPercent { get; set; } = 90;
    public int DelayMs { get; set; } = 500;
    public int Repeat { get; set; } = 1; // 0 = infinite

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    public static MacroConfig Load(string path) =>
        JsonSerializer.Deserialize<MacroConfig>(File.ReadAllText(path), Options) ?? new MacroConfig();
}
