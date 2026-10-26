using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Teleprompter;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new PrompterForm());
    }
}

static class Native
{
    public const uint WDA_NONE = 0x0, WDA_MONITOR = 0x1, WDA_EXCLUDEFROMCAPTURE = 0x11;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;
    public const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDOWN = 0xA1, WM_HOTKEY = 0x312;
    public const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
        HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    public const int ULW_ALPHA = 2;
    public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    /// <summary>Hides the window from screen capture. Returns a short status string.</summary>
    public static string ExcludeFromCapture(IntPtr hwnd)
    {
        if (Environment.GetCommandLineArgs().Contains("--show-in-capture"))
            return "Visible in capture (--show-in-capture)";
        if (SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
            return "Hidden from capture";
        // Pre-2004 Windows: the window shows up as a black box in captures instead.
        if (SetWindowDisplayAffinity(hwnd, WDA_MONITOR))
            return "Capture: black box (old Windows)";
        return "Capture exclusion FAILED";
    }

    /// <summary>Pushes a 32-bit ARGB bitmap to a layered window, keeping per-pixel alpha.</summary>
    public static void PushLayered(IntPtr hwnd, Bitmap bmp, Point location)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(memDc, hBmp);
        try
        {
            var size = new SIZE { CX = bmp.Width, CY = bmp.Height };
            var src = new POINT();
            var dst = new POINT { X = location.X, Y = location.Y };
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}

class Settings
{
    public string Text { get; set; } = "Paste your script here.\n\nPress E (or the Edit button) to edit this text. " +
        "Space plays and pauses, Up/Down change the speed.\n\nGlobal hotkeys work even while Zoom has focus:\n" +
        "Ctrl+Alt+Space  play / pause\nCtrl+Alt+Up / Down  speed\nCtrl+Alt+PgUp / PgDn  jump back / forward\n" +
        "Ctrl+Alt+Home  back to top\nCtrl+Alt+T  toggle click-through\nCtrl+Alt+H  hide / show window";
    public float FontSize { get; set; } = 28;
    public float Speed { get; set; } = 40; // pixels per second
    public float ClockSize { get; set; } = 16;
    public double BgOpacity { get; set; } = 0.6;   // black background only
    public double TextOpacity { get; set; } = 1.0; // white text only
    public bool Mirror { get; set; }
    public bool ShowBar { get; set; } = true;
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int W { get; set; } = 720;
    public int H { get; set; } = 320;

    static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Teleprompter", "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathName)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            File.WriteAllText(PathName, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

/// <summary>
/// The text window. It is a per-pixel-alpha layered window (UpdateLayeredWindow), so the black background
/// and the white text each get their own opacity. Such a window can't host child controls, so the buttons
/// live in a separate <see cref="BarForm"/> that follows it around.
/// </summary>
class PrompterForm : Form
{
    readonly Settings s = Settings.Load();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    readonly Stopwatch clock = new();
    readonly BarForm bar;
    Bitmap frame;
    Font textFont;
    float scroll;          // pixels scrolled from the start of the text
    float textHeight = -1; // cached layout height, -1 = needs measuring
    float elapsed;         // seconds spent playing since the last rewind
    bool playing, clickThrough;
    const int Grip = 8;

    public PrompterForm()
    {
        Text = "Teleprompter";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(260, 100);
        Size = new Size(s.W, s.H);
        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = s.X < 0 ? new Point(wa.Left + (wa.Width - s.W) / 2, wa.Top + 20) : new Point(s.X, s.Y);
        if (!SystemInformation.VirtualScreen.IntersectsWith(Bounds))
            Location = new Point(wa.Left + 20, wa.Top + 20);
        textFont = new Font("Segoe UI", s.FontSize, FontStyle.Bold);

        bar = new BarForm(this);
        timer.Tick += Tick;
        timer.Start();
        clock.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED;
            return cp;
        }
    }

    // ---------- window plumbing ----------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ExcludeFromCapture(Handle);
        RegisterHotkeys();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        bar.SetStatus(Native.ExcludeFromCapture(Handle));
        Render();
        if (s.ShowBar) bar.Show(this);
        bar.FollowOwner();
        RefreshBar();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        textHeight = -1;
        Render();
        bar?.FollowOwner();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        bar?.FollowOwner();
    }

    void RegisterHotkeys()
    {
        uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
        uint modsRepeat = Native.MOD_CONTROL | Native.MOD_ALT;
        Native.RegisterHotKey(Handle, 1, mods, (uint)Keys.Space);
        Native.RegisterHotKey(Handle, 2, modsRepeat, (uint)Keys.Up);
        Native.RegisterHotKey(Handle, 3, modsRepeat, (uint)Keys.Down);
        Native.RegisterHotKey(Handle, 4, mods, (uint)Keys.T);
        Native.RegisterHotKey(Handle, 5, mods, (uint)Keys.Home);
        Native.RegisterHotKey(Handle, 6, modsRepeat, (uint)Keys.PageUp);
        Native.RegisterHotKey(Handle, 7, modsRepeat, (uint)Keys.PageDown);
        Native.RegisterHotKey(Handle, 8, mods, (uint)Keys.H);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case 1: TogglePlay(); break;
                case 2: ChangeSpeed(+5); break;
                case 3: ChangeSpeed(-5); break;
                case 4: ToggleClickThrough(); break;
                case 5: Rewind(); break;
                case 6: ScrollBy(-Height * 0.5f); break;
                case 7: ScrollBy(Height * 0.5f); break;
                case 8: ToggleHidden(); break;
            }
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == Native.WM_NCHITTEST && m.Result == (IntPtr)Native.HTCLIENT)
        {
            var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            bool l = p.X < Grip, r = p.X >= ClientSize.Width - Grip, t = p.Y < Grip, b = p.Y >= ClientSize.Height - Grip;
            m.Result = (IntPtr)(t && l ? Native.HTTOPLEFT : t && r ? Native.HTTOPRIGHT : b && l ? Native.HTBOTTOMLEFT :
                b && r ? Native.HTBOTTOMRIGHT : l ? Native.HTLEFT : r ? Native.HTRIGHT : t ? Native.HTTOP :
                b ? Native.HTBOTTOM : Native.HTCLIENT);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks == 2) { OpenEditor(); return; }
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollBy(-e.Delta * textFont.Height / 120f);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        for (int i = 1; i <= 8; i++) Native.UnregisterHotKey(Handle, i);
        if (WindowState == FormWindowState.Normal)
        {
            s.X = Left; s.Y = Top; s.W = Width; s.H = Height;
        }
        s.Save();
        base.OnFormClosing(e);
    }

    // ---------- keys (also forwarded from the bar) ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

    public bool HandleKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Space: TogglePlay(); return true;
            case Keys.Up: ChangeSpeed(+5); return true;
            case Keys.Down: ChangeSpeed(-5); return true;
            case Keys.Oemplus: case Keys.Add: case Keys.Shift | Keys.Oemplus: ChangeFont(+2); return true;
            case Keys.OemMinus: case Keys.Subtract: case Keys.D6: ChangeFont(-2); return true;
            case Keys.Z: ChangeClockSize(-2); return true;
            case Keys.X: ChangeClockSize(+2); return true;
            case Keys.O: ChangeBgOpacity(-0.05); return true;
            case Keys.P: ChangeBgOpacity(+0.05); return true;
            case Keys.K: ChangeTextOpacity(-0.05); return true;
            case Keys.L: ChangeTextOpacity(+0.05); return true;
            case Keys.Home: Rewind(); return true;
            case Keys.PageUp: ScrollBy(-Height * 0.5f); return true;
            case Keys.PageDown: ScrollBy(Height * 0.5f); return true;
            case Keys.Left: ScrollBy(-textFont.Height); return true;
            case Keys.Right: ScrollBy(textFont.Height); return true;
            case Keys.E: OpenEditor(); return true;
            case Keys.Control | Keys.V: PasteClipboard(); return true;
            case Keys.M: ToggleMirror(); return true;
            case Keys.B: ToggleBar(); return true;
            case Keys.Escape: if (playing) TogglePlay(); return true;
        }
        return false;
    }

    // ---------- actions (public ones are used by the bar) ----------

    public void TogglePlay()
    {
        playing = !playing;
        if (playing && textHeight >= 0 && scroll >= textHeight) scroll = 0; // restart when at the end
        RefreshBar();
    }

    public void Rewind() { scroll = 0; elapsed = 0; Render(); }

    void ScrollBy(float dy)
    {
        scroll = Math.Clamp(scroll + dy, 0, Math.Max(0, textHeight));
        Render();
    }

    public void ChangeSpeed(float d) { s.Speed = Math.Clamp(s.Speed + d, 0, 600); RefreshBar(); }

    public void ChangeFont(float d)
    {
        float ratio = textHeight > 0 ? scroll / textHeight : 0;
        s.FontSize = Math.Clamp(s.FontSize + d, 8, 120);
        textFont.Dispose();
        textFont = new Font("Segoe UI", s.FontSize, FontStyle.Bold);
        MeasureText();
        scroll = ratio * textHeight; // keep roughly the same place in the script
        RefreshBar();
        Render();
    }

    public void ChangeClockSize(float d)
    {
        s.ClockSize = Math.Clamp(s.ClockSize + d, 8, 72);
        RefreshBar();
        Render();
    }

    public void ChangeBgOpacity(double d)
    {
        s.BgOpacity = Math.Round(Math.Clamp(s.BgOpacity + d, 0.0, 1.0), 2);
        RefreshBar();
        Render();
    }

    public void ChangeTextOpacity(double d)
    {
        s.TextOpacity = Math.Round(Math.Clamp(s.TextOpacity + d, 0.1, 1.0), 2);
        RefreshBar();
        Render();
    }

    public void ToggleMirror() { s.Mirror = !s.Mirror; RefreshBar(); Render(); }

    public void ToggleBar()
    {
        s.ShowBar = !s.ShowBar;
        if (s.ShowBar) { bar.Show(this); bar.FollowOwner(); } else bar.Hide();
    }

    void ToggleHidden()
    {
        Visible = !Visible;
        if (Visible) { Render(); if (s.ShowBar) bar.Show(this); }
    }

    public void ToggleClickThrough()
    {
        clickThrough = !clickThrough;
        int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
        ex = clickThrough ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
        RefreshBar();
    }

    void SetScript(string text)
    {
        s.Text = text.Replace("\r\n", "\n");
        scroll = 0;
        textHeight = -1;
        s.Save();
        Render();
    }

    public void PasteClipboard()
    {
        if (Clipboard.ContainsText()) SetScript(Clipboard.GetText());
    }

    public void OpenEditor()
    {
        bool wasPlaying = playing;
        if (playing) TogglePlay();
        using var ed = new EditorForm(s.Text);
        if (ed.ShowDialog(this) == DialogResult.OK) SetScript(ed.Result);
        else if (wasPlaying) TogglePlay();
    }

    void RefreshBar() => bar.UpdateFrom(playing, s, clickThrough);

    // ---------- scrolling + rendering ----------

    void Tick(object sender, EventArgs e)
    {
        float dt = (float)clock.Elapsed.TotalSeconds;
        clock.Restart();
        if (playing) elapsed += dt;
        if (!playing || s.Speed <= 0) return;
        if (textHeight < 0) MeasureText();
        scroll += s.Speed * Math.Min(dt, 0.1f);
        if (scroll >= textHeight) { scroll = textHeight; playing = false; RefreshBar(); }
        Render();
    }

    RectangleF TextRect => new(Width * 0.06f, 0, Math.Max(50, Width * 0.88f), 100000);

    void MeasureText()
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        textHeight = g.MeasureString(s.Text, textFont, (int)TextRect.Width).Height;
    }

    void Render()
    {
        if (!IsHandleCreated || !Visible || Width <= 0 || Height <= 0) return;
        if (frame == null || frame.Width != Width || frame.Height != Height)
        {
            frame?.Dispose();
            frame = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        }
        if (textHeight < 0) MeasureText();

        using (var g = Graphics.FromImage(frame))
        {
            // Alpha 1 instead of 0 keeps the window clickable/draggable even when the background is fully transparent.
            int bgA = Math.Max(1, (int)Math.Round(s.BgOpacity * 255));
            int textA = (int)Math.Round(s.TextOpacity * 255);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.FromArgb(bgA, 0, 0, 0));
            g.CompositingMode = CompositingMode.SourceOver;
            g.TextRenderingHint = TextRenderingHint.AntiAlias; // ClearType can't render onto a transparent surface
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // faint frame so the edges can be found for resizing
            using (var edge = new Pen(Color.FromArgb(70, 128, 128, 128)))
                g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);

            float readY = Height / 3f; // the reading line; text starts here and scrolls up past it

            var state = g.Save();
            if (s.Mirror) { g.TranslateTransform(Width, 0); g.ScaleTransform(-1, 1); }
            var r = TextRect;
            r.Y = readY - scroll;
            r.Height = textHeight + textFont.Height;
            using (var brush = new SolidBrush(Color.FromArgb(textA, 255, 255, 255)))
                g.DrawString(s.Text, textFont, brush, r);
            g.Restore(state);

            // stopwatch at the bottom-left, just above the button bar
            string time = TimeSpan.FromSeconds(elapsed).ToString(elapsed >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
            using (var timeFont = new Font("Consolas", s.ClockSize, FontStyle.Bold))
            using (var timeBrush = new SolidBrush(Color.FromArgb(textA, 255, 255, 255)))
            {
                SizeF size = g.MeasureString(time, timeFont);
                g.DrawString(time, timeFont, timeBrush, 10, Height - size.Height - 4);
            }

            // reading-line markers on both edges
            using var mark = new SolidBrush(Color.FromArgb(220, 255, 70, 70));
            float h = Math.Max(8, textFont.Height * 0.35f);
            float cy = readY + textFont.Height / 2f;
            g.FillPolygon(mark, new[] { new PointF(0, cy - h), new PointF(h * 1.2f, cy), new PointF(0, cy + h) });
            g.FillPolygon(mark, new[] { new PointF(Width, cy - h), new PointF(Width - h * 1.2f, cy), new PointF(Width, cy + h) });
        }
        Native.PushLayered(Handle, frame, Location);
    }
}

/// <summary>The button bar, an owned window that sits just below (or above) the text window.</summary>
class BarForm : Form
{
    readonly PrompterForm main;
    readonly FlowLayoutPanel panel = new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(2), WrapContents = true,
        BackColor = Color.FromArgb(32, 32, 32),
    };
    readonly ToolTip tips = new();
    readonly Button playBtn, mirrorBtn, clickBtn;
    readonly Label speedLbl, fontLbl, bgLbl, textLbl, clockLbl, statusLbl;

    public void SetStatus(string text) => statusLbl.Text = text;

    public BarForm(PrompterForm owner)
    {
        main = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = panel.BackColor;
        Opacity = 0.92;
        KeyPreview = true;
        Controls.Add(panel);

        playBtn = AddButton("▶ Play", (_, _) => main.TogglePlay(), "Play / pause (Space, Ctrl+Alt+Space)");
        AddButton("Top", (_, _) => main.Rewind(), "Back to top (Home, Ctrl+Alt+Home)");
        AddButton("Speed −", (_, _) => main.ChangeSpeed(-5), "Slower (Down, Ctrl+Alt+Down)");
        speedLbl = AddLabel();
        AddButton("+", (_, _) => main.ChangeSpeed(+5), "Faster (Up, Ctrl+Alt+Up)");
        AddButton("Font −", (_, _) => main.ChangeFont(-2), "Smaller text (-)");
        fontLbl = AddLabel();
        AddButton("+", (_, _) => main.ChangeFont(+2), "Bigger text (+)");
        AddButton("Background −", (_, _) => main.ChangeBgOpacity(-0.05), "More transparent background (O)");
        bgLbl = AddLabel();
        AddButton("+", (_, _) => main.ChangeBgOpacity(+0.05), "More opaque background (P)");
        AddButton("Text −", (_, _) => main.ChangeTextOpacity(-0.05), "More transparent text (K)");
        textLbl = AddLabel();
        AddButton("+", (_, _) => main.ChangeTextOpacity(+0.05), "More opaque text (L)");
        AddButton("Clock −", (_, _) => main.ChangeClockSize(-2), "Smaller stopwatch (Z)");
        clockLbl = AddLabel();
        AddButton("+", (_, _) => main.ChangeClockSize(+2), "Bigger stopwatch (X)");
        AddButton("Edit", (_, _) => main.OpenEditor(), "Edit / paste text (E, or double-click the text)");
        AddButton("Paste", (_, _) => main.PasteClipboard(), "Replace the text with the clipboard (Ctrl+V)");
        mirrorBtn = AddButton("Mirror", (_, _) => main.ToggleMirror(), "Mirror text horizontally (M)");
        clickBtn = AddButton("Click-through", (_, _) => main.ToggleClickThrough(),
            "Let clicks pass through the text window (Ctrl+Alt+T)");
        AddButton("Hide bar", (_, _) => main.ToggleBar(), "Hide this bar (B to bring it back)");
        AddButton("✕", (_, _) => main.Close(), "Quit");
        statusLbl = AddLabel();
        statusLbl.ForeColor = Color.LightGreen;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ExcludeFromCapture(Handle);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        main.HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>Matches the text window's width and sits under it, or above it near the bottom of the screen.</summary>
    public void FollowOwner()
    {
        if (!Visible) return;
        panel.MinimumSize = panel.MaximumSize = new Size(main.Width, 0);
        ClientSize = new Size(main.Width, panel.PreferredSize.Height);
        var wa = Screen.FromControl(main).WorkingArea;
        int y = main.Bottom + Height <= wa.Bottom ? main.Bottom : main.Top - Height;
        Location = new Point(main.Left, y);
    }

    public void UpdateFrom(bool playing, Settings s, bool clickThrough)
    {
        playBtn.Text = playing ? "❚❚ Pause" : "▶ Play";
        speedLbl.Text = $"{s.Speed:0}";
        fontLbl.Text = $"{s.FontSize:0}";
        bgLbl.Text = $"{s.BgOpacity * 100:0}%";
        textLbl.Text = $"{s.TextOpacity * 100:0}%";
        clockLbl.Text = $"{s.ClockSize:0}";
        mirrorBtn.BackColor = s.Mirror ? Color.SteelBlue : Color.FromArgb(55, 55, 55);
        clickBtn.BackColor = clickThrough ? Color.SteelBlue : Color.FromArgb(55, 55, 55);
        FollowOwner(); // labels may have changed width and wrapping
    }

    Button AddButton(string text, EventHandler onClick, string tip)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White, BackColor = Color.FromArgb(55, 55, 55), Margin = new Padding(2),
            Font = new Font("Segoe UI", 9), TabStop = false, Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        b.Click += onClick;
        tips.SetToolTip(b, tip);
        panel.Controls.Add(b);
        return b;
    }

    Label AddLabel()
    {
        var l = new Label
        {
            AutoSize = true, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9),
            Margin = new Padding(2, 8, 2, 2),
        };
        panel.Controls.Add(l);
        return l;
    }
}

class EditorForm : Form
{
    readonly TextBox box;
    public string Result => box.Text;

    public EditorForm(string text)
    {
        Text = "Edit script";
        TopMost = true;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(700, 520);
        box = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AcceptsReturn = true,
            AcceptsTab = true, Font = new Font("Segoe UI", 11), Text = text.Replace("\n", "\r\n"), MaxLength = 0,
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(4),
        };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var paste = new Button { Text = "Replace with clipboard", AutoSize = true };
        paste.Click += (_, _) => { if (Clipboard.ContainsText()) box.Text = Clipboard.GetText(); };
        var clear = new Button { Text = "Clear", AutoSize = true };
        clear.Click += (_, _) => box.Clear();
        buttons.Controls.AddRange(new Control[] { ok, cancel, paste, clear });
        Controls.Add(box);
        Controls.Add(buttons);
        CancelButton = cancel;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Native.ExcludeFromCapture(Handle); // keep the script out of recordings while editing too
        box.SelectionStart = 0;
        box.Focus();
    }
}
