using System.Reflection;

namespace MouseMacro;

public class MainForm : Form
{
    readonly List<SequenceItem> items = new();
    readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly ComboBox modeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    readonly NumericUpDown offX = new() { Maximum = 10000, Width = 70 };
    readonly NumericUpDown offY = new() { Maximum = 10000, Width = 70 };
    readonly NumericUpDown timeout = new() { Minimum = 0, Maximum = 600000, Increment = 500, Value = 10000, Width = 90 };
    readonly CheckBox stopIfNotFound = new() { Text = "못 찾으면 중지", Checked = true, AutoSize = true };
    readonly PictureBox tplBox = new() { SizeMode = PictureBoxSizeMode.AutoSize, Cursor = Cursors.Cross };
    readonly NumericUpDown matchPct = new() { Minimum = 1, Maximum = 100, Value = 90, Width = 70 };
    readonly NumericUpDown delay = new() { Minimum = 0, Maximum = 600000, Increment = 100, Value = 500, Width = 90 };
    readonly NumericUpDown repeat = new() { Minimum = 0, Maximum = 1000000, Value = 1, Width = 90 };
    readonly Button startBtn = new() { Text = "시작", AutoSize = true };
    readonly Button stopBtn = new() { Text = "중지 (F12)", AutoSize = true, Enabled = false };
    readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    readonly TextBox log = new() { Dock = DockStyle.Bottom, Height = 180, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

    Bitmap? tplBitmap;
    CancellationTokenSource? cts;
    bool updating;

    public MainForm()
    {
        Text = "이미지 인식 마우스 매크로";
        Width = 1100; Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        modeBox.Items.AddRange(new object[] { "이미지 전체 중심", "위쪽 정사각형 중심", "직접 지정 (X/Y)" });

        var addBtn = new Button { Text = "추가", AutoSize = true };
        var delBtn = new Button { Text = "삭제", AutoSize = true };
        var upBtn = new Button { Text = "▲", AutoSize = true };
        var downBtn = new Button { Text = "▼", AutoSize = true };
        var saveBtn = new Button { Text = "저장", AutoSize = true };
        var loadBtn = new Button { Text = "불러오기", AutoSize = true };

        var listButtons = Flow(addBtn, delBtn, upBtn, downBtn);
        var itemPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        itemPanel.Controls.Add(Flow(new Label { Text = "클릭 위치", AutoSize = true, Margin = new Padding(3, 6, 3, 0) }, modeBox));
        itemPanel.Controls.Add(Flow(Lbl("X"), offX, Lbl("Y"), offY));
        itemPanel.Controls.Add(Flow(Lbl("대기 시간(ms)"), timeout, stopIfNotFound));
        itemPanel.Controls.Add(new Label { Text = "미리보기를 클릭하면 클릭 위치가 지정됩니다", AutoSize = true });

        var tplScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.DimGray };
        tplScroll.Controls.Add(tplBox);

        var settings = Flow(Lbl("일치율(%)"), matchPct, Lbl("클릭 후 대기(ms)"), delay, Lbl("반복(0=무한)"), repeat, startBtn, stopBtn, saveBtn, loadBtn);
        settings.Dock = DockStyle.Top;

        var left = new TableLayoutPanel { Dock = DockStyle.Left, Width = 380, ColumnCount = 1, RowCount = 4 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        left.Controls.Add(list, 0, 0);
        left.Controls.Add(listButtons, 0, 1);
        left.Controls.Add(itemPanel, 0, 2);
        left.Controls.Add(tplScroll, 0, 3);

        Controls.Add(preview);
        Controls.Add(log);
        Controls.Add(left);
        Controls.Add(settings);

        list.SelectedIndexChanged += (_, _) => ShowSelected();
        addBtn.Click += (_, _) => AddImages();
        delBtn.Click += (_, _) => RemoveSelected();
        upBtn.Click += (_, _) => MoveItem(-1);
        downBtn.Click += (_, _) => MoveItem(1);
        modeBox.SelectedIndexChanged += (_, _) => ApplyItemEdits();
        offX.ValueChanged += (_, _) => ApplyItemEdits();
        offY.ValueChanged += (_, _) => ApplyItemEdits();
        timeout.ValueChanged += (_, _) => ApplyItemEdits();
        stopIfNotFound.CheckedChanged += (_, _) => ApplyItemEdits();
        tplBox.Paint += TplBox_Paint;
        tplBox.MouseDown += TplBox_MouseDown;
        startBtn.Click += async (_, _) => await StartAsync();
        stopBtn.Click += (_, _) => cts?.Cancel();
        saveBtn.Click += (_, _) => SaveConfig();
        loadBtn.Click += (_, _) => LoadConfig();

        AddDefaultItem();
        ShowSelected();
    }

    static Label Lbl(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

    static FlowLayoutPanel Flow(params Control[] c)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
        f.Controls.AddRange(c);
        return f;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Native.RegisterHotKey(Handle, Native.HOTKEY_ID, 0, Native.VK_F12))
            Log("F12 단축키 등록 실패 (다른 프로그램이 사용 중일 수 있습니다)");
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.UnregisterHotKey(Handle, Native.HOTKEY_ID);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == Native.HOTKEY_ID)
        {
            if (cts != null) { cts.Cancel(); Log("F12 비상 정지"); }
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        cts?.Cancel();
        base.OnFormClosing(e);
    }

    void Log(string msg)
    {
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }

    // ---- sequence editing ----

    void AddDefaultItem()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseMacro", "samples");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "doomyeon-sayong.png");
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("doomyeon-sayong.png");
            if (s == null) return;
            using (var fs = File.Create(path)) s.CopyTo(fs);
            items.Add(new SequenceItem { Path = path, Mode = ClickMode.UpperSquareCenter });
            RefreshList(0);
        }
        catch (Exception ex) { Log("기본 이미지 로드 실패: " + ex.Message); }
    }

    void RefreshList(int select)
    {
        updating = true;
        list.Items.Clear();
        for (int i = 0; i < items.Count; i++) list.Items.Add($"{i + 1}. {items[i].Name}");
        if (items.Count > 0) list.SelectedIndex = Math.Clamp(select, 0, items.Count - 1);
        updating = false;
        ShowSelected();
    }

    SequenceItem? Current => list.SelectedIndex >= 0 && list.SelectedIndex < items.Count ? items[list.SelectedIndex] : null;

    static Bitmap? LoadBitmap(string path)
    {
        try
        {
            using var img = Image.FromFile(path);
            return new Bitmap(img);
        }
        catch { return null; }
    }

    void ShowSelected()
    {
        if (updating) return;
        updating = true;
        var it = Current;
        bool has = it != null;
        modeBox.Enabled = offX.Enabled = offY.Enabled = timeout.Enabled = stopIfNotFound.Enabled = has;
        tplBitmap?.Dispose();
        tplBitmap = has ? LoadBitmap(it!.Path) : null;
        tplBox.Image = tplBitmap;
        if (has)
        {
            modeBox.SelectedIndex = (int)it!.Mode;
            var p = tplBitmap != null ? it.GetClickOffset(tplBitmap.Width, tplBitmap.Height) : new Point(it.OffsetX, it.OffsetY);
            offX.Value = Math.Clamp(p.X, 0, (int)offX.Maximum);
            offY.Value = Math.Clamp(p.Y, 0, (int)offY.Maximum);
            offX.ReadOnly = offY.ReadOnly = it.Mode != ClickMode.Custom;
            timeout.Value = Math.Clamp(it.TimeoutMs, 0, (int)timeout.Maximum);
            stopIfNotFound.Checked = it.StopIfNotFound;
        }
        updating = false;
        tplBox.Invalidate();
    }

    void ApplyItemEdits()
    {
        if (updating) return;
        var it = Current;
        if (it == null) return;
        it.Mode = (ClickMode)Math.Max(0, modeBox.SelectedIndex);
        if (it.Mode == ClickMode.Custom) { it.OffsetX = (int)offX.Value; it.OffsetY = (int)offY.Value; }
        it.TimeoutMs = (int)timeout.Value;
        it.StopIfNotFound = stopIfNotFound.Checked;
        ShowSelected();
    }

    void TplBox_MouseDown(object? sender, MouseEventArgs e)
    {
        var it = Current;
        if (it == null || tplBitmap == null) return;
        it.Mode = ClickMode.Custom;
        it.OffsetX = Math.Clamp(e.X, 0, tplBitmap.Width - 1);
        it.OffsetY = Math.Clamp(e.Y, 0, tplBitmap.Height - 1);
        ShowSelected();
    }

    void TplBox_Paint(object? sender, PaintEventArgs e)
    {
        var it = Current;
        if (it == null || tplBitmap == null) return;
        var p = it.GetClickOffset(tplBitmap.Width, tplBitmap.Height);
        using var pen = new Pen(Color.Red, 2);
        e.Graphics.DrawLine(pen, p.X - 8, p.Y, p.X + 8, p.Y);
        e.Graphics.DrawLine(pen, p.X, p.Y - 8, p.X, p.Y + 8);
        e.Graphics.DrawEllipse(pen, p.X - 5, p.Y - 5, 10, 10);
    }

    void AddImages()
    {
        using var dlg = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp|모든 파일|*.*", Multiselect = true };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        foreach (var f in dlg.FileNames)
            items.Add(new SequenceItem { Path = f, Mode = ClickMode.WholeCenter });
        RefreshList(items.Count - 1);
    }

    void RemoveSelected()
    {
        int i = list.SelectedIndex;
        if (i < 0) return;
        items.RemoveAt(i);
        RefreshList(i);
    }

    void MoveItem(int d)
    {
        int i = list.SelectedIndex, j = i + d;
        if (i < 0 || j < 0 || j >= items.Count) return;
        (items[i], items[j]) = (items[j], items[i]);
        RefreshList(j);
    }

    // ---- save / load ----

    MacroConfig ToConfig() => new()
    {
        Items = items.ToList(),
        MatchPercent = (int)matchPct.Value,
        DelayMs = (int)delay.Value,
        Repeat = (int)repeat.Value,
    };

    void SaveConfig()
    {
        using var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "macro.json" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try { ToConfig().Save(dlg.FileName); Log("저장 완료: " + dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "저장 실패"); }
    }

    void LoadConfig()
    {
        using var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            var c = MacroConfig.Load(dlg.FileName);
            items.Clear();
            items.AddRange(c.Items);
            matchPct.Value = Math.Clamp(c.MatchPercent, 1, 100);
            delay.Value = Math.Clamp(c.DelayMs, 0, (int)delay.Maximum);
            repeat.Value = Math.Clamp(c.Repeat, 0, (int)repeat.Maximum);
            RefreshList(0);
            Log("불러오기 완료: " + dlg.FileName);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "불러오기 실패"); }
    }

    // ---- running ----

    async Task StartAsync()
    {
        if (cts != null) return;
        if (items.Count == 0) { MessageBox.Show("이미지를 추가하세요."); return; }
        var cfg = ToConfig();
        var snapshot = cfg.Items.Select(i => new SequenceItem
        {
            Path = i.Path, Mode = i.Mode, OffsetX = i.OffsetX, OffsetY = i.OffsetY,
            TimeoutMs = i.TimeoutMs, StopIfNotFound = i.StopIfNotFound,
        }).ToList();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        startBtn.Enabled = false; stopBtn.Enabled = true;
        Log("시작");
        try
        {
            await Task.Run(() => RunLoop(snapshot, cfg, token));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log("오류: " + ex.Message); }
        Log("중지됨");
        cts.Dispose();
        cts = null;
        startBtn.Enabled = true; stopBtn.Enabled = false;
    }

    void UiLog(string m) => BeginInvoke(() => Log(m));

    void RunLoop(List<SequenceItem> seq, MacroConfig cfg, CancellationToken ct)
    {
        double threshold = cfg.MatchPercent / 100.0;
        var templates = new List<(Bitmap bmp, GrayImage gray)?>();
        foreach (var it in seq)
        {
            var b = LoadBitmap(it.Path);
            templates.Add(b == null ? null : (b, GrayImage.FromBitmap(b)));
            if (b == null) UiLog($"이미지를 읽을 수 없음: {it.Path}");
        }

        try
        {
            for (int round = 1; cfg.Repeat == 0 || round <= cfg.Repeat; round++)
            {
                UiLog($"반복 {round}" + (cfg.Repeat == 0 ? "" : $"/{cfg.Repeat}"));
                for (int i = 0; i < seq.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var it = seq[i];
                    var tpl = templates[i];
                    bool ok = tpl != null && WaitAndClick(it, tpl.Value.bmp, tpl.Value.gray, threshold, i + 1, ct);
                    if (!ok && it.StopIfNotFound) { UiLog($"{i + 1}번 이미지를 찾지 못해 중지"); return; }
                    if (ok) SleepCancelable(cfg.DelayMs, ct);
                }
            }
        }
        finally
        {
            foreach (var t in templates) t?.bmp.Dispose();
        }
    }

    bool WaitAndClick(SequenceItem it, Bitmap bmp, GrayImage tpl, double threshold, int no, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var shot = Native.CaptureScreen(out var bounds);
            var res = Matcher.Find(GrayImage.FromBitmap(shot), tpl);
            double score = res?.Score ?? 0;
            bool found = res != null && score >= threshold;
            Point click = default;
            if (res != null)
            {
                var off = it.GetClickOffset(bmp.Width, bmp.Height);
                click = new Point(bounds.X + res.Value.X + off.X, bounds.Y + res.Value.Y + off.Y);
            }
            var rect = res != null ? new Rectangle(res.Value.X, res.Value.Y, bmp.Width, bmp.Height) : Rectangle.Empty;
            ShowPreview(shot, rect, found);
            if (found)
            {
                UiLog($"[{no}] {it.Name} 찾음 (일치율 {score * 100:F1}%) 클릭 ({click.X}, {click.Y})");
                Native.Click(click.X, click.Y);
                return true;
            }
            if (sw.ElapsedMilliseconds >= it.TimeoutMs)
            {
                UiLog($"[{no}] {it.Name} 실패 (최고 일치율 {score * 100:F1}%, 제한 {threshold * 100:F0}%)");
                return false;
            }
            SleepCancelable(300, ct);
        }
    }

    void ShowPreview(Bitmap shot, Rectangle rect, bool found)
    {
        var copy = (Bitmap)shot.Clone();
        if (!rect.IsEmpty)
        {
            using var g = Graphics.FromImage(copy);
            using var pen = new Pen(found ? Color.Lime : Color.Red, 4);
            g.DrawRectangle(pen, rect);
        }
        try
        {
            BeginInvoke(() =>
            {
                var old = preview.Image;
                preview.Image = copy;
                old?.Dispose();
            });
        }
        catch { copy.Dispose(); }
    }

    static void SleepCancelable(int ms, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(Math.Max(0, ms))) ct.ThrowIfCancellationRequested();
    }
}
