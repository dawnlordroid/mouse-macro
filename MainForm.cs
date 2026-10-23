using System.ComponentModel;

namespace MouseMacro;

public class MainForm : Form
{
    readonly BindingList<Step> steps = new();
    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
    };
    readonly ComboBox monitorBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    readonly CheckBox relativeChk = new() { Text = "선택한 모니터 좌상단 기준 좌표", Checked = true, AutoSize = true };
    readonly CheckBox scaleChk = new() { Text = "해상도 비율 보정", Checked = true, AutoSize = true };
    readonly NumericUpDown refW = new() { Minimum = 1, Maximum = 20000, Value = 2560, Width = 70 };
    readonly NumericUpDown refH = new() { Minimum = 1, Maximum = 20000, Value = 1440, Width = 70 };
    readonly NumericUpDown repeat = new() { Minimum = 0, Maximum = 1000000, Value = 1, Width = 80 };
    readonly NumericUpDown startDelay = new() { Minimum = 0, Maximum = 600, Value = 3, Width = 60 };
    readonly NumericUpDown jitter = new() { Minimum = 0, Maximum = 60000, Increment = 50, Value = 0, Width = 70 };
    readonly Label mouseLabel = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
    readonly Button startBtn = new() { Text = "시작 (F10)", AutoSize = true };
    readonly Button stopBtn = new() { Text = "중지 (F12)", AutoSize = true, Enabled = false };
    readonly TextBox log = new() { Dock = DockStyle.Bottom, Height = 180, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly System.Windows.Forms.Timer posTimer = new() { Interval = 100 };

    Screen[] screens = Screen.AllScreens;
    CancellationTokenSource? cts;
    readonly Random rng = new();

    bool Running => cts != null;

    public MainForm()
    {
        Text = "좌표 클릭 마우스 매크로";
        Width = 960; Height = 720;
        StartPosition = FormStartPosition.CenterScreen;

        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Step.X), HeaderText = "X", Width = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Step.Y), HeaderText = "Y", Width = 70 });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(Step.Button),
            HeaderText = "버튼",
            DataSource = Enum.GetValues<MouseButtonKind>(),
            Width = 80,
            FlatStyle = FlatStyle.Flat,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Step.DelayMs), HeaderText = "이후 대기(ms)", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Step.Note), HeaderText = "메모", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.DataSource = steps;
        grid.DataError += (_, e) => e.ThrowException = false;

        var addBtn = new Button { Text = "추가", AutoSize = true };
        var delBtn = new Button { Text = "삭제", AutoSize = true };
        var upBtn = new Button { Text = "▲", AutoSize = true };
        var downBtn = new Button { Text = "▼", AutoSize = true };
        var captureBtn = new Button { Text = "좌표 캡처 (F8)", AutoSize = true };
        var moveBtn = new Button { Text = "테스트 클릭 (이동만)", AutoSize = true };
        var saveBtn = new Button { Text = "저장", AutoSize = true };
        var loadBtn = new Button { Text = "불러오기", AutoSize = true };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(Flow(Lbl("모니터"), monitorBox, relativeChk));
        top.Controls.Add(Flow(Lbl("기준 해상도"), refW, Lbl("x"), refH, scaleChk));
        top.Controls.Add(Flow(Lbl("반복(0=무한)"), repeat, Lbl("시작 전 대기(초)"), startDelay, Lbl("대기 랜덤 ±(ms)"), jitter));
        top.Controls.Add(Flow(startBtn, stopBtn, saveBtn, loadBtn));
        top.Controls.Add(Flow(addBtn, delBtn, upBtn, downBtn, captureBtn, moveBtn));
        top.Controls.Add(Flow(mouseLabel));

        Controls.Add(grid);
        Controls.Add(log);
        Controls.Add(top);

        addBtn.Click += (_, _) => AddStep();
        delBtn.Click += (_, _) => RemoveSelected();
        upBtn.Click += (_, _) => MoveStep(-1);
        downBtn.Click += (_, _) => MoveStep(1);
        captureBtn.Click += (_, _) => CaptureToSelected();
        moveBtn.Click += (_, _) => MoveOnly();
        startBtn.Click += (_, _) => Start();
        stopBtn.Click += (_, _) => Stop();
        saveBtn.Click += (_, _) => SaveDialog();
        loadBtn.Click += (_, _) => LoadDialog();
        monitorBox.SelectedIndexChanged += (_, _) => UpdateMouseLabel();
        relativeChk.CheckedChanged += (_, _) => UpdateMouseLabel();
        scaleChk.CheckedChanged += (_, _) => UpdateMouseLabel();
        refW.ValueChanged += (_, _) => UpdateMouseLabel();
        refH.ValueChanged += (_, _) => UpdateMouseLabel();
        posTimer.Tick += (_, _) => UpdateMouseLabel();

        FillMonitors("");
        try
        {
            ApplyConfig(File.Exists(MacroConfig.DefaultPath) ? MacroConfig.Load(MacroConfig.DefaultPath) : new MacroConfig());
        }
        catch (Exception ex)
        {
            ApplyConfig(new MacroConfig());
            Log("기본 설정 로드 실패: " + ex.Message);
        }
        posTimer.Start();
    }

    static Label Lbl(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

    static FlowLayoutPanel Flow(params Control[] c)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
        p.Controls.AddRange(c);
        return p;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Native.RegisterHotKey(Handle, Native.HOTKEY_STOP, 0, Native.VK_F12)) Log("F12 단축키 등록 실패");
        if (!Native.RegisterHotKey(Handle, Native.HOTKEY_CAPTURE, 0, Native.VK_F8)) Log("F8 단축키 등록 실패");
        if (!Native.RegisterHotKey(Handle, Native.HOTKEY_TOGGLE, 0, Native.VK_F10)) Log("F10 단축키 등록 실패");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        cts?.Cancel();
        try { CurrentConfig().Save(MacroConfig.DefaultPath); } catch { }
        Native.UnregisterHotKey(Handle, Native.HOTKEY_STOP);
        Native.UnregisterHotKey(Handle, Native.HOTKEY_CAPTURE);
        Native.UnregisterHotKey(Handle, Native.HOTKEY_TOGGLE);
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case Native.HOTKEY_STOP: Stop(); break;
                case Native.HOTKEY_CAPTURE: CaptureToSelected(); break;
                case Native.HOTKEY_TOGGLE: if (Running) Stop(); else Start(); break;
            }
        }
        base.WndProc(ref m);
    }

    void FillMonitors(string selectedDevice)
    {
        screens = Screen.AllScreens;
        monitorBox.Items.Clear();
        int sel = -1;
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            monitorBox.Items.Add($"모니터 {i + 1}{(s.Primary ? " (주 모니터)" : "")} - {s.Bounds.Width}x{s.Bounds.Height} @ ({s.Bounds.X},{s.Bounds.Y})");
            if (s.DeviceName == selectedDevice) sel = i;
        }
        if (sel < 0) sel = Math.Max(0, Array.FindIndex(screens, s => s.Primary));
        monitorBox.SelectedIndex = sel;
    }

    Rectangle MonitorBounds() =>
        screens[Math.Clamp(monitorBox.SelectedIndex, 0, screens.Length - 1)].Bounds;

    MacroConfig CurrentConfig() => new()
    {
        Steps = steps.Select(s => new Step { X = s.X, Y = s.Y, Button = s.Button, DelayMs = s.DelayMs, Note = s.Note }).ToList(),
        Repeat = (int)repeat.Value,
        StartDelaySec = (int)startDelay.Value,
        JitterMs = (int)jitter.Value,
        Monitor = monitorBox.SelectedIndex >= 0 ? screens[monitorBox.SelectedIndex].DeviceName : "",
        RelativeToMonitor = relativeChk.Checked,
        ScaleToResolution = scaleChk.Checked,
        RefWidth = (int)refW.Value,
        RefHeight = (int)refH.Value,
    };

    void ApplyConfig(MacroConfig c)
    {
        steps.Clear();
        foreach (var s in c.Steps) steps.Add(s);
        repeat.Value = Math.Clamp(c.Repeat, 0, (int)repeat.Maximum);
        startDelay.Value = Math.Clamp(c.StartDelaySec, 0, (int)startDelay.Maximum);
        jitter.Value = Math.Clamp(c.JitterMs, 0, (int)jitter.Maximum);
        relativeChk.Checked = c.RelativeToMonitor;
        scaleChk.Checked = c.ScaleToResolution;
        refW.Value = Math.Clamp(c.RefWidth, 1, (int)refW.Maximum);
        refH.Value = Math.Clamp(c.RefHeight, 1, (int)refH.Maximum);
        FillMonitors(c.Monitor);
    }

    void UpdateMouseLabel()
    {
        if (screens.Length == 0 || monitorBox.SelectedIndex < 0) return;
        var abs = Native.GetCursor();
        var b = MonitorBounds();
        var cfg = CurrentConfig();
        var rel = cfg.FromAbsolute(abs, b);
        mouseLabel.Text = $"마우스: 절대 ({abs.X}, {abs.Y}) / 모니터 기준 ({abs.X - b.X}, {abs.Y - b.Y}) / 기준해상도 환산 ({rel.X}, {rel.Y})";
    }

    void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg)); return; }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }

    int SelectedIndex() => grid.CurrentRow?.Index ?? -1;

    void AddStep()
    {
        grid.EndEdit();
        steps.Add(new Step());
        grid.CurrentCell = grid.Rows[steps.Count - 1].Cells[0];
    }

    void RemoveSelected()
    {
        grid.EndEdit();
        int i = SelectedIndex();
        if (i < 0) return;
        steps.RemoveAt(i);
        if (steps.Count > 0) grid.CurrentCell = grid.Rows[Math.Min(i, steps.Count - 1)].Cells[0];
    }

    void MoveStep(int d)
    {
        grid.EndEdit();
        int i = SelectedIndex(), j = i + d;
        if (i < 0 || j < 0 || j >= steps.Count) return;
        (steps[i], steps[j]) = (steps[j], steps[i]);
        grid.CurrentCell = grid.Rows[j].Cells[0];
    }

    void CaptureToSelected()
    {
        int i = SelectedIndex();
        if (i < 0 || i >= steps.Count) { Log("좌표를 넣을 단계를 먼저 선택하세요."); return; }
        var p = CurrentConfig().FromAbsolute(Native.GetCursor(), MonitorBounds());
        steps[i].X = p.X;
        steps[i].Y = p.Y;
        steps.ResetItem(i);
        Log($"단계 {i + 1} 좌표 캡처: ({p.X}, {p.Y})");
    }

    void MoveOnly()
    {
        grid.EndEdit();
        int i = SelectedIndex();
        if (i < 0 || i >= steps.Count) return;
        var abs = CurrentConfig().ToAbsolute(steps[i].X, steps[i].Y, MonitorBounds());
        Native.SetCursorPos(abs.X, abs.Y);
        Log($"단계 {i + 1} 이동만: 절대 ({abs.X}, {abs.Y})");
    }

    void SaveDialog()
    {
        grid.EndEdit();
        using var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "macro.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { CurrentConfig().Save(dlg.FileName); Log("저장: " + dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "저장 실패"); }
    }

    void LoadDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { ApplyConfig(MacroConfig.Load(dlg.FileName)); Log("불러오기: " + dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "불러오기 실패"); }
    }

    void Start()
    {
        if (Running) return;
        grid.EndEdit();
        if (steps.Count == 0) { Log("단계가 없습니다."); return; }
        var cfg = CurrentConfig();
        var bounds = MonitorBounds();
        var token = (cts = new CancellationTokenSource()).Token;
        startBtn.Enabled = false;
        stopBtn.Enabled = true;
        Task.Run(() => RunMacro(cfg, bounds, token)).ContinueWith(_ => BeginInvoke(() =>
        {
            cts?.Dispose();
            cts = null;
            startBtn.Enabled = true;
            stopBtn.Enabled = false;
        }));
    }

    void Stop()
    {
        if (cts == null) return;
        cts.Cancel();
        Log("중지 요청");
    }

    void RunMacro(MacroConfig cfg, Rectangle bounds, CancellationToken ct)
    {
        try
        {
            if (cfg.StartDelaySec > 0)
            {
                Log($"{cfg.StartDelaySec}초 후 시작합니다. 게임 창으로 전환하세요.");
                Wait(cfg.StartDelaySec * 1000, ct);
            }
            Log("시작");
            for (int r = 1; cfg.Repeat == 0 || r <= cfg.Repeat; r++)
            {
                for (int i = 0; i < cfg.Steps.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var s = cfg.Steps[i];
                    var abs = cfg.ToAbsolute(s.X, s.Y, bounds);
                    Native.Click(abs.X, abs.Y, s.Button);
                    Log($"[{r}회차] 단계 {i + 1}: {s.Button} 클릭 절대 ({abs.X}, {abs.Y}) {s.Note}");
                    int d = s.DelayMs;
                    if (cfg.JitterMs > 0) d += rng.Next(-cfg.JitterMs, cfg.JitterMs + 1);
                    Wait(Math.Max(0, d), ct);
                }
            }
            Log("완료");
        }
        catch (OperationCanceledException) { Log("중지됨"); }
        catch (Exception ex) { Log("오류: " + ex.Message); }
    }

    static void Wait(int ms, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(ms)) throw new OperationCanceledException(ct);
    }
}
