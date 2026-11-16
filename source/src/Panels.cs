using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Imaging;

namespace PrismDock
{
    /// Live liquid-glass popup. Opens out of the dock with a spring (scale + slide + fade);
    /// the window region follows the glass so the corners stay smooth and clicks pass around it.
    abstract class PanelWindow : Window
    {
        protected readonly double PW, PH;
        protected const double Pad = 18;
        protected readonly GlassSurface surf;
        readonly Canvas canvas = new Canvas();
        readonly Grid content;
        readonly ScaleTransform sc = new ScaleTransform(1, 1);
        readonly TranslateTransform tr = new TranslateTransform();
        readonly Spring prog;
        protected IntPtr hwnd; protected double scale = 1; bool fromTop, built;
        protected BackdropHost host;
        protected bool CenterOrigin;
        public bool ForceForeground;
        Int32Rect lastRgn = new Int32Rect(-1, -1, -1, -1);
        public bool IsOpen; public DateTime ClosedAt = DateTime.MinValue;
        protected static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        const double Radius = 26;

        protected PanelWindow(double w, double h)
        {
            PW = w; PH = h;
            WindowStyle = WindowStyle.None; AllowsTransparency = false; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            Background = Brushes.Transparent; Width = w + Pad * 2; Height = h + Pad * 2; FontFamily = Ui.TextFont;
            surf = new GlassSurface(Radius, 22) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Width = w, Height = h, Margin = new Thickness(Pad) };
            var tg = new TransformGroup(); tg.Children.Add(sc); tg.Children.Add(tr);
            surf.RenderTransform = tg;
            content = new Grid { Width = w, Height = h };
            canvas.Children.Add(content);
            surf.Body.Children.Add(canvas);
            var root = new Grid(); root.Children.Add(surf);
            Content = root;
            prog = new Spring(0, OnProgress, 260, 0.74);
            LocationChanged += (s, e) => { if (host != null) host.UpdateAll(); };
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
                scale = Glass.Scale(this);
                Glass.SetupDwm(this, hwnd);
                Glass.ClipTo(hwnd, Rect.Empty, 0, scale, ref lastRgn);
                host = new BackdropHost(this, true);
                surf.Attach(host);
            };
            Deactivated += (s, e) => CloseAnimated();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) CloseAnimated(); };
        }

        protected abstract UIElement Build();
        protected virtual void Opened() { }
        protected virtual void Closed2() { }

        public void ApplyTheme()
        {
            surf.ApplyTheme();
            if (host != null) host.Configure();
            if (built) { content.Children.Clear(); content.Children.Add(Build()); }
        }

        /// Renders the panel to a PNG without showing a window (used by --snapshot).
        public void Snapshot(string file) { Snapshot(file, null); }
        public void Snapshot(string file, Action after)
        {
            built = true;
            var hostS = new GlassSurface(Radius) { Width = PW, Height = PH };
            hostS.Body.Children.Add(Build());
            if (after != null)
            {
                IsOpen = true;
                after();
                var end = DateTime.Now.AddMilliseconds(2500);
                while (DateTime.Now < end)
                {
                    var f = new DispatcherFrame();
                    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => f.Continue = false));
                    Dispatcher.PushFrame(f);
                    System.Threading.Thread.Sleep(20);
                }
            }
            var bg = new Grid { Width = PW + 40, Height = PH + 40, Background = new ImageBrush(Wallpaper.Blurred ?? (ImageSource)Icons.Letter("x")) { Stretch = Stretch.UniformToFill } };
            hostS.Margin = new Thickness(20);
            bg.Children.Add(hostS);
            bg.Measure(new Size(bg.Width, bg.Height)); bg.Arrange(new Rect(0, 0, bg.Width, bg.Height)); bg.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)bg.Width * 2, (int)bg.Height * 2, 192, 192, PixelFormats.Pbgra32);
            rtb.Render(bg);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(file)) enc.Save(fs);
        }

        protected void Rebuild() { content.Children.Clear(); content.Children.Add(Build()); }

        void OnProgress(double p)
        {
            if (surf == null) return;
            double s = 0.92 + 0.08 * p;
            sc.CenterX = PW / 2; sc.CenterY = CenterOrigin ? PH / 2 : (fromTop ? 0 : PH);
            sc.ScaleX = s; sc.ScaleY = s;
            tr.Y = CenterOrigin ? (1 - p) * -10 : (1 - p) * (fromTop ? -16 : 16);
            surf.Opacity = Ui.Clamp(p * 1.8, 0, 1);
            content.Opacity = Ui.Clamp((p - 0.2) / 0.8, 0, 1);
            if (hwnd != IntPtr.Zero)
            {
                var b = p <= 0.001 ? Rect.Empty : Glass.Bounds(surf, this);
                Glass.ClipTo(hwnd, b, Radius * s, scale, ref lastRgn);
            }
            if (host != null) host.UpdateAll();
        }

        public void OpenAt(double anchorX, double edgeY, bool top)
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            Place(Ui.Clamp(anchorX - PW / 2, 8, sw - PW - 8), top ? edgeY + 10 : edgeY - PH - 10, top);
        }

        /// x, y: where the glass itself goes (DIP, screen).
        protected void Place(double x, double y, bool top)
        {
            fromTop = top;
            if (!built) { built = true; Rebuild(); }
            if (hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            Left = x - Pad; Top = y - Pad;
            prog.OnSettle = null;
            prog.Snap(0);
            IsOpen = true;
            host.Configure();
            Show();
            if (ForceForeground)
            {
                // opened from a global key: the foreground lock must be lifted (ALT tap trick)
                Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
                Native.SetForegroundWindow(hwnd);
                Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
                ForceForeground = false;
            }
            Activate();
            host.Start();
            Opened();
            prog.To(1, 300, 0.72);
        }

        public void CloseAnimated()
        {
            if (!IsOpen) return;
            IsOpen = false; ClosedAt = DateTime.Now;
            Closed2();
            prog.OnSettle = () => { if (!IsOpen) { Hide(); if (host != null) host.Stop(); } };
            prog.To(0, 500, 1.0);
        }

        // ---- shared building blocks ----
        protected static TextBlock Header(string s)
        {
            var t = Ui.Text(s, 17, FontWeights.SemiBold, Ui.Br(Theme.Cur.Fg));
            t.Margin = new Thickness(0, 0, 0, 12);
            return t;
        }
        protected static TextBlock Label(string s)
        {
            var t = Ui.Text(s, 12, FontWeights.SemiBold, Ui.Br(Theme.Cur.Sub));
            t.Margin = new Thickness(0, 10, 0, 6);
            return t;
        }
        protected static Border Card(UIElement child)
        {
            var t = Theme.Cur;
            return new Border { Background = Ui.Br(Ui.A(t.Fg, t.Dark ? 0.07 : 0.05)), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 10, 14, 10), Child = child, Margin = new Thickness(0, 0, 0, 8) };
        }
    }

    // ================= Calendar =================
    class CalendarPanel : PanelWindow
    {
        TextBlock time, date, month; UniformGrid days; TranslateTransform slide = new TranslateTransform();
        DateTime shown; DispatcherTimer timer;
        public CalendarPanel() : base(336, 400) { }

        protected override UIElement Build()
        {
            var t = Theme.Cur;
            var sp = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
            time = Ui.Text("", 44, FontWeights.Light, Ui.Br(t.Fg));
            date = Ui.Text("", 14, FontWeights.Normal, Ui.Br(t.Sub));
            date.Margin = new Thickness(2, -2, 0, 14);
            sp.Children.Add(time); sp.Children.Add(date);

            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            month = Ui.Text("", 15, FontWeights.SemiBold, Ui.Br(t.Fg));
            head.Children.Add(month);
            var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            nav.Children.Add(Ui.Button(Ui.Glyph("", 12, Ui.Br(t.Fg)), () => Go(-1), null, 30, 30, 15));
            nav.Children.Add(Ui.Button(Ui.Glyph("", 12, Ui.Br(t.Fg)), () => { shown = DateTime.Today; Fill(0); }, "Bugün", 30, 30, 15));
            nav.Children.Add(Ui.Button(Ui.Glyph("", 12, Ui.Br(t.Fg)), () => Go(1), null, 30, 30, 15));
            head.Children.Add(nav);
            sp.Children.Add(head);

            var wd = new UniformGrid { Columns = 7 };
            foreach (var d in new[] { "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" })
            {
                var tb = Ui.Text(d, 11, FontWeights.SemiBold, Ui.Br(t.Sub)); tb.HorizontalAlignment = HorizontalAlignment.Center;
                wd.Children.Add(tb);
            }
            sp.Children.Add(wd);
            days = new UniformGrid { Columns = 7, Rows = 6, Margin = new Thickness(0, 6, 0, 0), RenderTransform = slide };
            var clip = new Grid { ClipToBounds = true }; clip.Children.Add(days);
            sp.Children.Add(clip);
            shown = DateTime.Today;
            Fill(0);
            Tick();
            return sp;
        }

        void Go(int d) { shown = shown.AddMonths(d); Fill(d); }

        void Fill(int dir)
        {
            var t = Theme.Cur;
            month.Text = Tr.TextInfo.ToTitleCase(shown.ToString("MMMM yyyy", Tr));
            days.Children.Clear();
            var first = new DateTime(shown.Year, shown.Month, 1);
            int offset = ((int)first.DayOfWeek + 6) % 7;
            var start = first.AddDays(-offset);
            for (int i = 0; i < 42; i++)
            {
                var d = start.AddDays(i);
                bool today = d == DateTime.Today, cur = d.Month == shown.Month;
                var tb = Ui.Text(d.Day.ToString(), 13, today ? FontWeights.Bold : FontWeights.Normal,
                    Ui.Br(today ? (t.Accent == Colors.White ? Color.FromRgb(20, 20, 24) : Colors.White) : Ui.A(t.Fg, cur ? 1 : 0.32)));
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                var b = Ui.Button(tb, null, null, 34, 34, 17);
                if (today) b.Background = Ui.Br(t.Accent);
                b.HorizontalAlignment = HorizontalAlignment.Center;
                b.Margin = new Thickness(0, 1, 0, 1);
                days.Children.Add(b);
            }
            if (dir != 0)
            {
                slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 40, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = Ui.SoftSpring });
                days.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            }
        }

        void Tick()
        {
            var n = DateTime.Now;
            time.Text = n.ToString(App.S.Seconds ? "HH:mm:ss" : "HH:mm");
            date.Text = n.ToString("d MMMM yyyy, dddd", Tr);
        }

        protected override void Opened()
        {
            if (shown.Month != DateTime.Today.Month || shown.Year != DateTime.Today.Year) { shown = DateTime.Today; Fill(0); }
            Tick();
            if (timer == null) { timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (s, e) => Tick(); }
            timer.Start();
        }
        protected override void Closed2() { if (timer != null) timer.Stop(); }
    }

    // ================= System =================
    static class SysStats
    {
        static long pi, pk, pu;
        public static double Cpu()
        {
            long i, k, u;
            if (!Native.GetSystemTimes(out i, out k, out u)) return 0;
            long di = i - pi, dk = k - pk, du = u - pu;
            pi = i; pk = k; pu = u;
            long total = dk + du;
            if (total <= 0) return 0;
            return Ui.Clamp((double)(total - di) / total, 0, 1);
        }
        public static double Ram(out double usedGb, out double totalGb)
        {
            var m = new Native.MEMORYSTATUSEX(); m.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(m);
            Native.GlobalMemoryStatusEx(ref m);
            totalGb = m.ullTotalPhys / 1073741824.0; usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            return totalGb > 0 ? usedGb / totalGb : 0;
        }
        public static int Battery(out bool charging)
        {
            Native.SYSTEM_POWER_STATUS s; charging = false;
            if (!Native.GetSystemPowerStatus(out s) || s.BatteryFlag == 128 || s.BatteryFlag == 255) return -1;
            charging = s.ACLineStatus == 1;
            return s.BatteryLifePercent > 100 ? -1 : s.BatteryLifePercent;
        }
    }

    class SystemPanel : PanelWindow
    {
        Border cpuF, ramF, diskF, batF; TextBlock cpuT, ramT, diskT, batT, upT; DispatcherTimer timer;
        public SystemPanel() : base(352, 430) { }

        FrameworkElement Row(string name, out Border fill, out TextBlock val)
        {
            var t = Theme.Cur;
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            g.Children.Add(Ui.Text(name, 13, FontWeights.SemiBold, Ui.Br(t.Fg)));
            val = Ui.Text("", 12, FontWeights.Normal, Ui.Br(t.Sub)); val.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(val);
            var bar = Ui.Bar(282, 6, out fill); bar.Margin = new Thickness(0, 6, 0, 0); bar.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(bar, 1); g.Children.Add(bar);
            return g;
        }

        protected override UIElement Build()
        {
            var t = Theme.Cur;
            var sp = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
            sp.Children.Add(Header("Sistem"));
            var stats = new StackPanel();
            stats.Children.Add(Row("İşlemci", out cpuF, out cpuT));
            stats.Children.Add(Row("Bellek", out ramF, out ramT));
            stats.Children.Add(Row("Disk C:", out diskF, out diskT));
            bool ch; if (SysStats.Battery(out ch) >= 0) stats.Children.Add(Row("Pil", out batF, out batT));
            upT = Ui.Text("", 11, FontWeights.Normal, Ui.Br(t.Sub)); upT.Margin = new Thickness(0, 6, 0, 0);
            stats.Children.Add(upT);
            sp.Children.Add(Card(stats));

            var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, 0, 0) };
            grid.Children.Add(Action("", "Kilitle", () => Native.LockWorkStation()));
            grid.Children.Add(Action("", "Çöp kutusu", () => Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0)));
            grid.Children.Add(Action("", "Görev Yön.", () => Run("taskmgr.exe")));
            grid.Children.Add(Action("", "Ayarlar", () => Run("ms-settings:")));
            grid.Children.Add(Action("", "Ağ", () => Run("ms-availablenetworks:")));
            grid.Children.Add(Action("", "Ses", () => Run("ms-settings:sound")));
            grid.Children.Add(Action("", "Bildirimler", () => Native.Keys(0x5B, 0x41)));
            grid.Children.Add(Action("", "Masaüstü", () => Native.Keys(0x5B, 0x44)));
            sp.Children.Add(grid);
            return sp;
        }

        FrameworkElement Action(string glyph, string name, Action a)
        {
            var t = Theme.Cur;
            var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(Ui.Glyph(glyph, 18, Ui.Br(t.Fg)));
            var l = Ui.Text(name, 10.5, FontWeights.Normal, Ui.Br(t.Sub)); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 6, 0, 0);
            st.Children.Add(l);
            var b = Ui.Button(st, () => { CloseAnimated(); a(); }, null, 72, 66, 16);
            b.Margin = new Thickness(2);
            return b;
        }

        static void Run(string s) { try { Process.Start(new ProcessStartInfo(s) { UseShellExecute = true }); } catch { } }

        void Set(Border f, TextBlock tb, double frac, string text)
        {
            if (f == null) return;
            Ui.Anim(f, WidthProperty, 282 * Ui.Clamp(frac, 0, 1), 600, Ui.Out);
            tb.Text = text;
        }

        void Update()
        {
            double c = SysStats.Cpu();
            Set(cpuF, cpuT, c, Math.Round(c * 100) + "%");
            double u, tot; double r = SysStats.Ram(out u, out tot);
            Set(ramF, ramT, r, u.ToString("0.0") + " / " + tot.ToString("0.0") + " GB");
            try
            {
                var d = new DriveInfo("C");
                double tg = d.TotalSize / 1073741824.0, fg = d.AvailableFreeSpace / 1073741824.0;
                Set(diskF, diskT, (tg - fg) / tg, fg.ToString("0") + " GB boş");
            }
            catch { }
            bool ch; int b = SysStats.Battery(out ch);
            if (b >= 0) Set(batF, batT, b / 100.0, b + "%" + (ch ? " · şarj oluyor" : ""));
            var up = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            upT.Text = "Çalışma süresi: " + (int)up.TotalDays + " g " + up.Hours + " sa " + up.Minutes + " dk";
        }

        protected override void Opened()
        {
            Update();
            if (timer == null) { timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) }; timer.Tick += (s, e) => Update(); }
            timer.Start();
        }
        protected override void Closed2() { if (timer != null) timer.Stop(); }
    }

    // ================= Notes =================
    class NotesPanel : PanelWindow
    {
        TextBox box; DispatcherTimer saveTimer;
        static string FilePath { get { return System.IO.Path.Combine(Settings.Dir, "notes.txt"); } }
        public NotesPanel() : base(352, 340) { }

        protected override UIElement Build()
        {
            var t = Theme.Cur;
            var g = new Grid { Margin = new Thickness(20, 18, 20, 18) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.Children.Add(Header("Notlar"));
            string txt = ""; try { if (File.Exists(FilePath)) txt = File.ReadAllText(FilePath); } catch { }
            box = new TextBox
            {
                Text = txt, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Foreground = Ui.Br(t.Fg), CaretBrush = Ui.Br(t.Accent), FontSize = 14, FontFamily = Ui.TextFont,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, SelectionBrush = Ui.Br(t.Accent)
            };
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); Save(); };
            box.TextChanged += (s, e) => { saveTimer.Stop(); saveTimer.Start(); };
            var card = Card(box); card.Margin = new Thickness(0); card.Padding = new Thickness(10);
            Grid.SetRow(card, 1); g.Children.Add(card);
            return g;
        }

        void Save() { try { File.WriteAllText(FilePath, box.Text); } catch { } }
        protected override void Opened() { Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.CaretIndex = box.Text.Length; }), DispatcherPriority.Input); }
        protected override void Closed2() { Save(); }
    }

    // ================= Pomodoro =================
    static class Pomodoro
    {
        public static int Minutes = 25;
        public static int Remaining = 25 * 60;
        public static bool Running;
        public static event Action Changed;
        public static event Action Finished;
        static DispatcherTimer timer;

        public static void Toggle()
        {
            if (timer == null) { timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (s, e) => Tick(); }
            Running = !Running;
            if (Running) timer.Start(); else timer.Stop();
            Fire();
        }
        public static void Set(int min) { Minutes = min; Remaining = min * 60; Running = false; if (timer != null) timer.Stop(); Fire(); }
        public static void Reset() { Set(Minutes); }
        static void Tick()
        {
            Remaining--;
            if (Remaining <= 0)
            {
                Running = false; timer.Stop(); Remaining = Minutes * 60;
                SystemSounds.Asterisk.Play();
                if (Finished != null) Finished();
            }
            Fire();
        }
        static void Fire() { if (Changed != null) Changed(); }
        public static string Text { get { return (Remaining / 60).ToString("00") + ":" + (Remaining % 60).ToString("00"); } }
        public static double Fraction { get { return 1.0 - (double)Remaining / (Minutes * 60); } }
    }

    class PomodoroPanel : PanelWindow
    {
        System.Windows.Shapes.Path arc; TextBlock timeT; Border startB; TextBlock startT;
        const double R = 88;
        public PomodoroPanel() : base(316, 400) { Pomodoro.Changed += () => { if (IsOpen) Sync(); }; }

        protected override UIElement Build()
        {
            var t = Theme.Cur;
            var sp = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
            sp.Children.Add(Header("Odak Zamanlayıcı"));
            var ring = new Grid { Width = R * 2 + 12, Height = R * 2 + 12, HorizontalAlignment = HorizontalAlignment.Center };
            ring.Children.Add(new Ellipse { Width = R * 2, Height = R * 2, Stroke = Ui.Br(Ui.A(t.Fg, 0.12)), StrokeThickness = 8 });
            arc = new System.Windows.Shapes.Path { Stroke = Ui.Br(t.Accent), StrokeThickness = 8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = R * 2 + 12, Height = R * 2 + 12 };
            ring.Children.Add(arc);
            timeT = Ui.Text("", 40, FontWeights.Light, Ui.Br(t.Fg)); timeT.HorizontalAlignment = HorizontalAlignment.Center;
            ring.Children.Add(timeT);
            sp.Children.Add(ring);

            var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 12) };
            chips.Children.Add(Ui.Chip("Odak 25", Pomodoro.Minutes == 25, () => { Pomodoro.Set(25); Rebuild(); }));
            chips.Children.Add(Ui.Chip("Mola 5", Pomodoro.Minutes == 5, () => { Pomodoro.Set(5); Rebuild(); }));
            chips.Children.Add(Ui.Chip("Uzun 15", Pomodoro.Minutes == 15, () => { Pomodoro.Set(15); Rebuild(); }));
            sp.Children.Add(chips);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            startT = Ui.Text("", 13, FontWeights.SemiBold, Ui.Br(t.Accent == Colors.White ? Color.FromRgb(20, 20, 24) : Colors.White));
            startT.HorizontalAlignment = HorizontalAlignment.Center;
            startB = Ui.Button(startT, () => Pomodoro.Toggle(), null, 120, 38, 19);
            startB.Background = Ui.Br(t.Accent);
            startB.Margin = new Thickness(0, 0, 8, 0);
            btns.Children.Add(startB);
            var rt = Ui.Text("Sıfırla", 13, FontWeights.SemiBold, Ui.Br(t.Fg)); rt.HorizontalAlignment = HorizontalAlignment.Center;
            var rb = Ui.Button(rt, () => Pomodoro.Reset(), null, 90, 38, 19);
            rb.BorderBrush = Ui.Br(Ui.A(t.Fg, 0.2)); rb.BorderThickness = new Thickness(1);
            btns.Children.Add(rb);
            sp.Children.Add(btns);
            Sync();
            return sp;
        }

        void Sync()
        {
            if (timeT == null) return;
            timeT.Text = Pomodoro.Text;
            startT.Text = Pomodoro.Running ? "Duraklat" : "Başlat";
            arc.Data = Arc(Pomodoro.Fraction);
        }

        static Geometry Arc(double f)
        {
            double c = R + 6;
            if (f <= 0.0005) return Geometry.Empty;
            if (f >= 0.9995) return new EllipseGeometry(new Point(c, c), R, R);
            double ang = f * Math.PI * 2;
            var end = new Point(c + R * Math.Sin(ang), c - R * Math.Cos(ang));
            var fig = new PathFigure { StartPoint = new Point(c, c - R), IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(R, R), 0, f > 0.5, SweepDirection.Clockwise, true));
            var g = new PathGeometry(); g.Figures.Add(fig); g.Freeze();
            return g;
        }

        protected override void Opened() { Sync(); }
    }

    // ================= Settings =================
    class SettingsPanel : PanelWindow
    {
        public SettingsPanel() : base(740, 590) { }

        protected override UIElement Build()
        {
            var t = Theme.Cur; var S = App.S;
            var root = new Grid { Margin = new Thickness(22, 18, 22, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(332) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            var head = Header("PrismDock Ayarları"); Grid.SetColumnSpan(head, 3); root.Children.Add(head);

            // ---- left: look
            var sp = new StackPanel();
            Grid.SetRow(sp, 1); root.Children.Add(sp);
            sp.Children.Add(Label("TEMA"));
            var wrap = new WrapPanel();
            foreach (var th in Theme.All) wrap.Children.Add(Swatch(th, th.Id == t.Id));
            sp.Children.Add(wrap);

            sp.Children.Add(Label("CAM"));
            var gl = new StackPanel();
            gl.Children.Add(SliderRow("Işık yoğunluğu", 0, 150, S.GlassLight, v => S.GlassLight = (int)Math.Round(v)));
            gl.Children.Add(SliderRow("Kırılma gücü", 0, 200, S.Refraction, v => S.Refraction = (int)Math.Round(v)));
            var glc = Card(gl); glc.Padding = new Thickness(14, 8, 14, 6);
            sp.Children.Add(glc);

            sp.Children.Add(Label("KONUM  ·  STİL  ·  SİMGE BOYUTU"));
            sp.Children.Add(Chips(new[] { "Alt", "Üst" }, S.Top ? 1 : 0, i => S.Top = i == 1));
            var st = Chips(new[] { "Hap (yüzen)", "Tam genişlik" }, S.FullWidth ? 1 : 0, i => S.FullWidth = i == 1); st.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(st);
            int si = S.IconSize <= 34 ? 0 : (S.IconSize >= 46 ? 2 : 1);
            var sz = Chips(new[] { "Küçük", "Orta", "Büyük" }, si, i => S.IconSize = i == 0 ? 32 : (i == 1 ? 40 : 48)); sz.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(sz);

            // ---- right: behaviour
            var rp = new StackPanel();
            Grid.SetRow(rp, 1); Grid.SetColumn(rp, 2); root.Children.Add(rp);
            rp.Children.Add(Label("DAVRANIŞ"));
            var tg = new StackPanel();
            tg.Children.Add(ToggleRow("Dinamik ada (üstte saat)", S.Island, v => S.Island = v));
            tg.Children.Add(ToggleRow("Dock'u otomatik gizle (ada ile aç)", S.AutoHide, v => S.AutoHide = v));
            tg.Children.Add(ToggleRow("Win tuşu PrismDock menüsünü açsın", S.WinKeyMenu, v => S.WinKeyMenu = v));
            tg.Children.Add(ToggleRow("Arama kısayolu  (Alt + Boşluk)", S.SearchHotkey, v => S.SearchHotkey = v));
            tg.Children.Add(ToggleRow("Canlı cam", S.LiveGlass, v => S.LiveGlass = v));
            tg.Children.Add(ToggleRow("Büyütme efekti", S.Magnify, v => S.Magnify = v));
            tg.Children.Add(ToggleRow("Windows görev çubuğunu gizle", S.HideTaskbar, v => S.HideTaskbar = v));
            tg.Children.Add(ToggleRow("Ekranda yer ayır", S.Reserve, v => S.Reserve = v));
            tg.Children.Add(ToggleRow("Saniyeleri göster", S.Seconds, v => S.Seconds = v));
            tg.Children.Add(ToggleRow("Windows ile başlat", S.AutoStart, v => S.AutoStart = v));
            var card = Card(tg); card.Padding = new Thickness(14, 6, 14, 6);
            rp.Children.Add(card);

            var at = Ui.Text("+  Dock'a uygulama ekle…", 13, FontWeights.SemiBold, Ui.Br(t.Fg));
            at.HorizontalAlignment = HorizontalAlignment.Center;
            var ab = Ui.Button(at, () => { CloseAnimated(); App.Dock.OpenAppPicker(); }, null, 0, 38, 19);
            ab.Background = Ui.Br(Ui.A(t.Fg, 0.08)); ab.BorderBrush = Ui.Br(Ui.A(t.Fg, 0.16)); ab.BorderThickness = new Thickness(1);
            ab.Margin = new Thickness(0, 4, 0, 4);
            rp.Children.Add(ab);

            var qt = Ui.Text("PrismDock'tan çık", 13, FontWeights.SemiBold, Ui.Br(Color.FromRgb(255, 90, 90)));
            qt.HorizontalAlignment = HorizontalAlignment.Center;
            var q = Ui.Button(qt, () => App.Quit(), null, 0, 34, 17);
            rp.Children.Add(q);
            return root;
        }

        FrameworkElement SliderRow(string name, double min, double max, double val, Action<double> set)
        {
            var t = Theme.Cur;
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            g.Children.Add(Ui.Text(name, 13, FontWeights.Normal, Ui.Br(t.Fg)));
            var vt = Ui.Text("%" + Math.Round(val), 12, FontWeights.SemiBold, Ui.Br(t.Sub)); vt.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(vt);
            var sl = Ui.Slider(276, min, max, val, null, v => vt.Text = "%" + Math.Round(v), v => { set(v); Dispatcher.BeginInvoke(new Action(() => App.ApplySettings()), DispatcherPriority.Background); });
            sl.Margin = new Thickness(-4, 2, 0, 0);
            Grid.SetRow(sl, 1); g.Children.Add(sl);
            return g;
        }

        void Changed() { App.ApplySettings(); Rebuild(); }

        FrameworkElement Chips(string[] names, int sel, Action<int> set)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                p.Children.Add(Ui.Chip(names[i], i == sel, () => { if (k == sel) return; set(k); Changed(); }));
            }
            return p;
        }

        FrameworkElement ToggleRow(string text, bool v, Action<bool> set)
        {
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.Children.Add(Ui.Text(text, 13, FontWeights.Normal, Ui.Br(Theme.Cur.Fg)));
            var tgl = Ui.Toggle(v, b => { set(b); Dispatcher.BeginInvoke(new Action(() => App.ApplySettings()), DispatcherPriority.Background); });
            tgl.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(tgl);
            return g;
        }

        FrameworkElement Swatch(Theme th, bool sel)
        {
            var cur = Theme.Cur;
            var g = new Grid { Width = 44, Height = 44 };
            g.Children.Add(new Ellipse { Fill = Ui.Br(th.Dark ? Color.FromRgb(40, 42, 50) : Color.FromRgb(236, 236, 242)) });
            var tint = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            for (int i = 0; i < th.Tint.Length; i++)
            {
                var c = th.Tint[i];
                tint.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Min(255, c.A * 2.4), c.R, c.G, c.B), th.Tint.Length == 1 ? 0 : (double)i / (th.Tint.Length - 1)));
            }
            g.Children.Add(new Ellipse { Fill = tint });
            g.Children.Add(new Ellipse { Fill = new LinearGradientBrush(th.Sheen, Colors.Transparent, 90), Margin = new Thickness(3, 2, 3, 18) });
            g.Children.Add(new Ellipse { Stroke = Ui.Br(sel ? cur.Accent : th.Rim1), StrokeThickness = sel ? 2.5 : 1 });
            var st = new StackPanel();
            st.Children.Add(g);
            var l = Ui.Text(th.Name, 10.5, sel ? FontWeights.SemiBold : FontWeights.Normal, Ui.Br(sel ? cur.Fg : cur.Sub));
            l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 5, 0, 0);
            st.Children.Add(l);
            var b = Ui.Button(st, () => { App.S.Theme = th.Id; Changed(); }, null, 78, 74, 14);
            b.Padding = new Thickness(0, 4, 0, 0);
            return b;
        }
    }
}
