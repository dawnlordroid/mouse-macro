using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

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
    readonly NumericUpDown repeat = new() { Minimum = 0, Maximum = 1000000, Value = 0, Width = 80 };
    readonly NumericUpDown maxRepeats = new() { Minimum = 0, Maximum = 1000000, Value = 0, Width = 80 };
    readonly NumericUpDown threshold = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.01m, Value = 0.90m, Width = 60 };
    readonly NumericUpDown checkTimeout = new() { Minimum = 0, Maximum = 600000, Increment = 500, Value = 5000, Width = 80 };
    readonly PictureBox refPic = new() { Width = 160, Height = 90, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    readonly PictureBox lastPic = new() { Width = 160, Height = 90, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    Bitmap? refImage;
    readonly NumericUpDown startDelay = new() { Minimum = 0, Maximum = 600, Value = 3, Width = 60 };
    readonly NumericUpDown jitter = new() { Minimum = 0, Maximum = 60000, Increment = 50, Value = 0, Width = 70 };
    readonly CheckBox perStepChk = new() { Text = "단계별 개별 딜레이 사용", Checked = true, AutoSize = true };
    readonly NumericUpDown globalInterval = new() { Minimum = 0, Maximum = 600000, Increment = 100, Value = 1000, Width = 80, Enabled = false };
    readonly NumericUpDown speedMul = new() { Minimum = 0.25m, Maximum = 5m, DecimalPlaces = 2, Increment = 0.25m, Value = 1m, Width = 60 };
    readonly NumericUpDown moveWait = new() { Minimum = 0, Maximum = 10000, Increment = 10, Value = 50, Width = 70 };
    readonly NumericUpDown pressMs = new() { Minimum = 0, Maximum = 10000, Increment = 10, Value = 30, Width = 70 };
    readonly NumericUpDown checkInterval = new() { Minimum = 10, Maximum = 60000, Increment = 50, Value = 250, Width = 70 };
    readonly CheckBox middleStopChk = new() { Text = "마우스 휠 클릭(가운데 버튼)으로도 중지", Checked = true, AutoSize = true };
    readonly Label mouseLabel = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
    readonly Button startBtn = new() { Text = "시작 (F10)", AutoSize = true };
    readonly Button stopBtn = new() { Text = "중지 (F12)", AutoSize = true, Enabled = false };
    readonly TextBox log = new() { Dock = DockStyle.Bottom, Height = 180, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly System.Windows.Forms.Timer posTimer = new() { Interval = 100 };

    Screen[] screens = Screen.AllScreens;
    CancellationTokenSource? cts;
    readonly Random rng = new();
    readonly object stopLock = new();

    // Hook delegates are kept in fields so they are not garbage collected.
    Native.LowLevelProc? kbProc, mouseProc;
    IntPtr kbHook, mouseHook;
    bool f12HookDown;
    int f12PollPrev;
    volatile bool middleStopEnabled = true;
    Thread? pollThread;
    volatile bool closing;
    static readonly bool IsAdmin = CheckAdmin();

    static bool CheckAdmin()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    bool Running => cts != null;

    public MainForm()
    {
        Text = $"좌표 클릭 마우스 매크로 - 관리자 권한: {(IsAdmin ? "예" : "아니오")}";
        Width = 960; Height = 820;
        StartPosition = FormStartPosition.CenterScreen;

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(Step.Type),
            HeaderText = "종류",
            DataSource = Enum.GetValues<StepType>(),
            Width = 70,
            FlatStyle = FlatStyle.Flat,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Step.X), HeaderText = "X", Width = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Step.Y), HeaderText = "Y", Width = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Step.W), HeaderText = "W(확인)", Width = 60 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Step.H), HeaderText = "H(확인)", Width = 60 });
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
        var refBtn = new Button { Text = "기준 캡처", AutoSize = true };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(Flow(Lbl("모니터"), monitorBox, relativeChk));
        top.Controls.Add(Flow(Lbl("기준 해상도"), refW, Lbl("x"), refH, scaleChk));
        top.Controls.Add(Flow(Lbl("반복(0=무한)"), repeat, Lbl("시작 전 대기(초)"), startDelay, Lbl("최대 반복(0=무제한)"), maxRepeats));
        top.Controls.Add(Flow(Lbl("유사도 임계값"), threshold, middleStopChk));
        var speedBox = new GroupBox { Text = "속도", Dock = DockStyle.Top, AutoSize = true };
        var speedFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        speedFlow.Controls.Add(Flow(perStepChk, Lbl("클릭 간격(ms)"), globalInterval, Lbl("속도 배율(x)"), speedMul, Lbl("랜덤 ±(ms)"), jitter));
        speedFlow.Controls.Add(Flow(Lbl("이동 후 대기(ms)"), moveWait, Lbl("누름 시간(ms)"), pressMs));
        speedFlow.Controls.Add(Flow(Lbl("확인 재시도 간격(ms)"), checkInterval, Lbl("확인 타임아웃(ms)"), checkTimeout));
        speedBox.Controls.Add(speedFlow);
        top.Controls.Add(speedBox);
        top.Controls.Add(Flow(refBtn, Lbl("기준:"), refPic, Lbl("최근 캡처:"), lastPic));
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
        refBtn.Click += (_, _) => CaptureReference();
        startBtn.Click += (_, _) => Start();
        stopBtn.Click += (_, _) => StopFrom("중지 버튼");
        saveBtn.Click += (_, _) => SaveDialog();
        loadBtn.Click += (_, _) => LoadDialog();
        perStepChk.CheckedChanged += (_, _) => globalInterval.Enabled = !perStepChk.Checked;
        middleStopChk.CheckedChanged += (_, _) => middleStopEnabled = middleStopChk.Checked;
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
        LoadReference();
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
        InstallHooks();
        StartPollThread();
        if (!Native.RegisterHotKey(Handle, Native.HOTKEY_STOP, 0, Native.VK_F12)) Log("F12 단축키 등록 실패");
        if (!Native.RegisterHotKey(Handle, Native.HOTKEY_CAPTURE, 0, Native.VK_F8)) Log("F8 단축키 등록 실패");
        if (!Native.RegisterHotKey(Handle, Native.HOTKEY_TOGGLE, 0, Native.VK_F10)) Log("F10 단축키 등록 실패");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true;
        cts?.Cancel();
        if (kbHook != IntPtr.Zero) Native.UnhookWindowsHookEx(kbHook);
        if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
        kbHook = mouseHook = IntPtr.Zero;
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
                case Native.HOTKEY_STOP: StopFrom("F12"); break;
                case Native.HOTKEY_CAPTURE: CaptureToSelected(); break;
                case Native.HOTKEY_TOGGLE: if (Running) StopFrom("F10"); else Start(); break;
            }
        }
        base.WndProc(ref m);
    }

    void InstallHooks()
    {
        var mod = Native.GetModuleHandle(null);
        kbProc = KeyboardHook;
        mouseProc = MouseHook;
        kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, kbProc, mod, 0);
        mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, mod, 0);
        if (kbHook == IntPtr.Zero) Log("키보드 훅 설치 실패 (폴링으로 F12 중지)");
        if (mouseHook == IntPtr.Zero) Log("마우스 훅 설치 실패");
    }

    IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            uint vk = (uint)Marshal.ReadInt32(lParam);
            int msg = wParam.ToInt32();
            bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
            if (vk == Native.VK_F12)
            {
                if (down && !f12HookDown) { f12HookDown = true; StopFrom("F12"); }
                else if (up) f12HookDown = false;
            }
            else if (down && vk == Native.VK_F8) BeginInvoke(CaptureToSelected);
            else if (down && vk == Native.VK_F10) BeginInvoke(() => { if (Running) StopFrom("F10"); else Start(); });
        }
        return Native.CallNextHookEx(kbHook, nCode, wParam, lParam);
    }

    IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == Native.WM_MBUTTONDOWN && middleStopEnabled)
            StopFrom("가운데 버튼");
        return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }

    // Edge-detected F12 polling, shared by the poll thread and every wait of the run loop.
    bool F12Pressed()
    {
        int now = Native.IsKeyDown(Native.VK_F12) ? 1 : 0;
        return Interlocked.Exchange(ref f12PollPrev, now) == 0 && now == 1;
    }

    void StartPollThread()
    {
        if (pollThread != null) return;
        pollThread = new Thread(() =>
        {
            while (!closing)
            {
                if (F12Pressed()) StopFrom("F12");
                Thread.Sleep(15);
            }
        }) { IsBackground = true, Name = "F12Poll" };
        pollThread.Start();
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
        Steps = steps.Select(s => new Step { Type = s.Type, X = s.X, Y = s.Y, W = s.W, H = s.H, Button = s.Button, DelayMs = s.DelayMs, Note = s.Note }).ToList(),
        Repeat = (int)repeat.Value,
        MaxRepeats = (int)maxRepeats.Value,
        Threshold = (double)threshold.Value,
        CheckTimeoutMs = (int)checkTimeout.Value,
        StartDelaySec = (int)startDelay.Value,
        JitterMs = (int)jitter.Value,
        UsePerStepDelay = perStepChk.Checked,
        GlobalIntervalMs = (int)globalInterval.Value,
        SpeedMultiplier = (double)speedMul.Value,
        MoveWaitMs = (int)moveWait.Value,
        PressMs = (int)pressMs.Value,
        CheckIntervalMs = (int)checkInterval.Value,
        MiddleButtonStop = middleStopChk.Checked,
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
        maxRepeats.Value = Math.Clamp(c.MaxRepeats, 0, (int)maxRepeats.Maximum);
        threshold.Value = (decimal)Math.Clamp(c.Threshold, 0, 1);
        checkTimeout.Value = Math.Clamp(c.CheckTimeoutMs, 0, (int)checkTimeout.Maximum);
        startDelay.Value = Math.Clamp(c.StartDelaySec, 0, (int)startDelay.Maximum);
        jitter.Value = Math.Clamp(c.JitterMs, 0, (int)jitter.Maximum);
        perStepChk.Checked = c.UsePerStepDelay;
        globalInterval.Value = Math.Clamp(c.GlobalIntervalMs, 0, (int)globalInterval.Maximum);
        speedMul.Value = (decimal)Math.Clamp(c.SpeedMultiplier, (double)speedMul.Minimum, (double)speedMul.Maximum);
        moveWait.Value = Math.Clamp(c.MoveWaitMs, 0, (int)moveWait.Maximum);
        pressMs.Value = Math.Clamp(c.PressMs, 0, (int)pressMs.Maximum);
        checkInterval.Value = Math.Clamp(c.CheckIntervalMs, (int)checkInterval.Minimum, (int)checkInterval.Maximum);
        middleStopChk.Checked = c.MiddleButtonStop;
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

    Rectangle AbsRegion(MacroConfig cfg, Step s, Rectangle bounds)
    {
        var p1 = cfg.ToAbsolute(s.X, s.Y, bounds);
        var p2 = cfg.ToAbsolute(s.X + s.W, s.Y + s.H, bounds);
        return new Rectangle(p1.X, p1.Y, Math.Max(1, p2.X - p1.X), Math.Max(1, p2.Y - p1.Y));
    }

    void SetPreview(PictureBox pb, Bitmap? bmp)
    {
        var old = pb.Image;
        pb.Image = bmp;
        old?.Dispose();
    }

    void LoadReference()
    {
        try
        {
            if (!File.Exists(MacroConfig.RefImagePath)) return;
            using var fs = File.OpenRead(MacroConfig.RefImagePath);
            using var tmp = new Bitmap(fs);
            refImage = new Bitmap(tmp);
            SetPreview(refPic, new Bitmap(refImage));
            Log("기준 이미지 로드: " + MacroConfig.RefImagePath);
        }
        catch (Exception ex) { Log("기준 이미지 로드 실패: " + ex.Message); }
    }

    void CaptureReference()
    {
        if (Running) { Log("매크로를 먼저 중지하세요."); return; }
        grid.EndEdit();
        var step = steps.ElementAtOrDefault(SelectedIndex()) is { Type: StepType.Check } sel
            ? sel : steps.FirstOrDefault(s => s.Type == StepType.Check);
        if (step == null) { Log("확인 단계가 없습니다."); return; }
        try
        {
            var r = AbsRegion(CurrentConfig(), step, MonitorBounds());
            using var bmp = ImageCheck.Capture(r);
            Directory.CreateDirectory(MacroConfig.DefaultDir);
            bmp.Save(MacroConfig.RefImagePath, System.Drawing.Imaging.ImageFormat.Png);
            refImage?.Dispose();
            refImage = new Bitmap(bmp);
            SetPreview(refPic, new Bitmap(bmp));
            Log($"기준 캡처 저장: 절대 ({r.X}, {r.Y}, {r.Width}x{r.Height}) → {MacroConfig.RefImagePath}");
        }
        catch (Exception ex) { Log("기준 캡처 실패: " + ex.Message); }
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
        Bitmap? reference = null;
        if (cfg.Steps.Any(s => s.Type == StepType.Check))
        {
            if (refImage == null)
            {
                Log("먼저 기준 캡처를 하세요");
                MessageBox.Show(this, "먼저 기준 캡처를 하세요", "기준 이미지 없음");
                return;
            }
            reference = new Bitmap(refImage);
        }
        var token = (cts = new CancellationTokenSource()).Token;
        startBtn.Enabled = false;
        stopBtn.Enabled = true;
        Task.Run(() => RunMacro(cfg, bounds, reference, token)).ContinueWith(_ => BeginInvoke(() =>
        {
            reference?.Dispose();
            cts?.Dispose();
            cts = null;
            startBtn.Enabled = true;
            stopBtn.Enabled = false;
        }));
    }

    // Thread-safe and idempotent per run: safe to call from hooks, the poll thread and the UI.
    void StopFrom(string source)
    {
        lock (stopLock)
        {
            var c = cts;
            if (c == null) return;
            try
            {
                if (c.IsCancellationRequested) return;
                c.Cancel();
            }
            catch (ObjectDisposedException) { return; }
        }
        Log($"{source}로 중지");
        System.Media.SystemSounds.Beep.Play();
    }

    void RunMacro(MacroConfig cfg, Rectangle bounds, Bitmap? reference, CancellationToken ct)
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
                if (cfg.MaxRepeats > 0 && r > cfg.MaxRepeats) { Log($"최대 반복 {cfg.MaxRepeats}회 도달"); break; }
                for (int i = 0; i < cfg.Steps.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var s = cfg.Steps[i];
                    if (s.Type == StepType.Check)
                    {
                        if (!RunCheck(cfg, bounds, reference!, s, r, i, ct)) return;
                        continue;
                    }
                    var abs = cfg.ToAbsolute(s.X, s.Y, bounds);
                    Native.Click(abs.X, abs.Y, s.Button, cfg.MoveWaitMs, cfg.PressMs, ct);
                    int baseDelay = cfg.UsePerStepDelay ? s.DelayMs : cfg.GlobalIntervalMs;
                    double mul = cfg.SpeedMultiplier > 0 ? cfg.SpeedMultiplier : 1.0;
                    int d = (int)Math.Round(baseDelay / mul);
                    if (cfg.JitterMs > 0) d += rng.Next(-cfg.JitterMs, cfg.JitterMs + 1);
                    d = Math.Max(0, d);
                    Log($"[{r}회차] 단계 {i + 1}: {s.Button} 클릭 절대 ({abs.X}, {abs.Y}) {s.Note} / 실제 대기 {d}ms (기준 {baseDelay}ms, {mul:0.##}x)");
                    Wait(d, ct);
                }
            }
            Log("완료");
        }
        catch (OperationCanceledException) { Log("중지됨"); }
        catch (Exception ex) { Log("오류: " + ex.Message); }
        finally { Native.ReleaseHeldButton(); }
    }

    bool RunCheck(MacroConfig cfg, Rectangle bounds, Bitmap reference, Step s, int round, int idx, CancellationToken ct)
    {
        var region = AbsRegion(cfg, s, bounds);
        var deadline = DateTime.UtcNow.AddMilliseconds(cfg.CheckTimeoutMs);
        double score;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var cap = ImageCheck.Capture(region);
            score = ImageCheck.Similarity(cap, reference);
            var preview = new Bitmap(cap);
            BeginInvoke(() => SetPreview(lastPic, preview));
            Log($"[{round}회차] 단계 {idx + 1}: 확인 유사도 {score:F3} (임계값 {cfg.Threshold:F2})");
            if (score >= cfg.Threshold) return true;
            if (DateTime.UtcNow >= deadline) break;
            Wait(Math.Max(10, cfg.CheckIntervalMs), ct);
        }
        var msg = $"28일이 아님 → 중지 (유사도 {score:F3})";
        Log(msg);
        System.Media.SystemSounds.Exclamation.Play();
        BeginInvoke(() => MessageBox.Show(this, msg, "매크로 중지", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        return false;
    }

    // Waits in short slices so a cancel or an F12 press is noticed within ~15 ms.
    void Wait(int ms, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (F12Pressed()) StopFrom("F12");
            ct.ThrowIfCancellationRequested();
            long left = ms - sw.ElapsedMilliseconds;
            if (left <= 0) return;
            if (ct.WaitHandle.WaitOne((int)Math.Min(left, 15))) throw new OperationCanceledException(ct);
        }
    }
}
