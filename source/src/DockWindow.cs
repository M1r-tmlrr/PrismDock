using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PrismDock
{
    class DockWindow : Window
    {
        IntPtr hwnd; double scale = 1;
        readonly Grid root = new Grid();
        GlassSurface surf; Canvas canvas; FrameworkElement content; StackPanel appsPanel;
        readonly TranslateTransform surfShift = new TranslateTransform();
        readonly ScaleTransform surfScale = new ScaleTransform(1, 1);
        bool autoHidden;
        DispatcherTimer autoTimer;
        const int AutoHideSeconds = 18;
        readonly List<DockApp> apps = new List<DockApp>();
        double S, pillH, margin, winH, radius;
        bool built, hiddenFs;
        DispatcherTimer refreshTimer, slowTimer, clockTimer, statTimer;
        uint shellMsg, taskbarCreatedMsg;
        Native.WinEventProc winProc; IntPtr hookFg, hookMin, hookShow;
        DispatcherTimer fsCheck;
        TextBlock clockTime, clockDate, pomoText, batText, playGlyph, mediaTitle, mediaArtist; Border cpuFill, ramFill, mediaThumb; FrameworkElement batWidget, pomoWidget, mediaInfo;
        int statTicks;
        DateTime lastLayout;
        BackdropHost host;
        Spring entrance;
        Int32Rect lastRgn = new Int32Rect(-1, -1, -1, -1);
        const int HotkeyId = 0x5D01;

        CalendarPanel cal; SystemPanel sys; NotesPanel notes; PomodoroPanel pomo; SettingsPanel settingsP; SearchWindow search; AppPickerPanel picker; StartPanel startP; TrayPanel trayP;
        StackPanel trayInline;
        IEnumerable<PanelWindow> Panels { get { return new PanelWindow[] { cal, sys, notes, pomo, settingsP, search, picker, startP, trayP }.Where(p => p != null); } }

        public DockWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = false; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            ShowActivated = false; Background = Brushes.Transparent; AllowDrop = true; FontFamily = Ui.TextFont;
            Title = "PrismDock";
            Content = root;
            SourceInitialized += OnSource;
            Drop += OnDrop;
            DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
            LayoutUpdated += (s, e) => UpdateRegion();

            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            refreshTimer.Tick += (s, e) => { refreshTimer.Stop(); Refresh(); };
            slowTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
            slowTimer.Tick += (s, e) => { Refresh(); if (App.S.HideTaskbar) Taskbar.Enforce(); };
            var enforce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            enforce.Tick += (s, e) => { if (App.S.HideTaskbar) Taskbar.Enforce(); };
            enforce.Start();
            clockTimer = new DispatcherTimer();
            clockTimer.Tick += (s, e) => UpdateClock();
            statTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            statTimer.Tick += (s, e) => UpdateStats();
            entrance = new Spring(0, v =>
            {
                surfShift.Y = v;
                double f = winH > 0 ? Math.Min(1, Math.Abs(v) / winH) : 0;
                if (surf != null) surf.Opacity = 1 - 0.8 * f;
                surfScale.ScaleX = surfScale.ScaleY = 1 - 0.07 * f;
                UpdateRegion();
            }, 170, 0.8);
            autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoHideSeconds) };
            autoTimer.Tick += (s, e) => AutoHideTick();

            Pomodoro.Changed += UpdatePomo;
            Pomodoro.Finished += () => { if (pomoWidget != null) Pulse(pomoWidget); };
            Media.Changed += UpdateMedia;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => { Layout(); }), DispatcherPriority.Background);

            LoadPins();
            Build();
        }

        // ---------------------------------------------------------------- setup
        void OnSource(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            scale = Glass.Scale(this);
            Glass.SetupDwm(this, hwnd);
            Glass.ClipTo(hwnd, Rect.Empty, 0, scale, ref lastRgn);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            shellMsg = Native.RegisterWindowMessage("SHELLHOOK");
            taskbarCreatedMsg = Native.RegisterWindowMessage("TaskbarCreated");
            Native.RegisterShellHookWindow(hwnd);
            winProc = OnWinEvent;
            hookFg = Native.SetWinEventHook(3, 3, IntPtr.Zero, winProc, 0, 0, 0);          // EVENT_SYSTEM_FOREGROUND
            hookMin = Native.SetWinEventHook(0x16, 0x17, IntPtr.Zero, winProc, 0, 0, 0);   // MINIMIZESTART/END
            host = new BackdropHost(this, false);
            AttachGlass();
            Layout();
            Refresh();
            slowTimer.Start();
            UpdateClock(); UpdateStats(); statTimer.Start();
            ApplyHotkey();
            Tray.Changed += FillTray;
            var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            later.Tick += (s2, e2) => { later.Stop(); Media.Init(Dispatcher); Tray.Start(Dispatcher); };
            later.Start();
            // explorer re-showing its taskbar (Windows 11 does this) is caught the moment it happens
            HookExplorerShow();
            Entrance();
            WatchForeground(Native.GetForegroundWindow());
            // warm the app catalog for search / picker in the background
            var warm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            warm.Tick += (s, e2) =>
            {
                warm.Stop(); AppCatalog.Load(Dispatcher);
                string open = Environment.GetEnvironmentVariable("PRISM_OPEN"); // dev: preview a panel without input
                if (open == "search") OpenSearch();
                else if (open == "picker") OpenAppPicker();
                else if (open == "settings") OpenSettings();
                else if (open == "calendar") { var c = cal ?? (cal = new CalendarPanel()); c.OpenAt(SystemParameters.PrimaryScreenWidth - 200, Edge, App.S.Top); }
                if (Environment.GetEnvironmentVariable("PRISM_BENCH") != null) Bench();
                if (Environment.GetEnvironmentVariable("PRISM_DRAGTEST") != null) DragTest();
                if (open != null && open.StartsWith("search:")) { OpenSearch(); search.DebugQuery(open.Substring(7)); }
            };
            warm.Start();
        }

        void AttachGlass()
        {
            if (host == null) return;
            host.Clear();
            surf.Attach(host);
            foreach (var a in apps) if (a.View != null && !a.Removing) a.View.Attach(host);
        }

        public void Shutdown()
        {
            try
            {
                Native.UnregisterHotKey(hwnd, HotkeyId);
                WinKeyHook.Enable(false);
                Taskbar.Unreserve(hwnd);
                Native.DeregisterShellHookWindow(hwnd);
                if (hookFg != IntPtr.Zero) Native.UnhookWinEvent(hookFg);
                if (hookMin != IntPtr.Zero) Native.UnhookWinEvent(hookMin);
                if (hookShow != IntPtr.Zero) Native.UnhookWinEvent(hookShow);
            }
            catch { }
        }

        public void ApplyHotkey()
        {
            if (hwnd == IntPtr.Zero) return;
            Native.UnregisterHotKey(hwnd, HotkeyId);
            if (App.S.SearchHotkey) Native.RegisterHotKey(hwnd, HotkeyId, 0x1 | 0x4000, 0x20); // Alt + Space, no repeat
            WinKeyHook.Pressed = () => OpenStart(true);
            WinKeyHook.Enable(App.S.WinKeyMenu);
        }

        void LoadPins()
        {
            foreach (var p in App.S.Pinned)
                if (DockApp.IsShellPath(p) || File.Exists(p) || Directory.Exists(p)) apps.Add(DockApp.FromPath(p));
        }

        // ---------------------------------------------------------------- layout
        void Layout()
        {
            var s = App.S;
            double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
            Left = 0; Width = sw; Height = winH;
            if (!s.FullWidth && targetW > 0) { double want = Math.Min(sw, Math.Ceiling(targetW * 1.04 + margin * 2 + 8)); Left = Math.Round((sw - want) / 2); Width = want; }
            if (s.HideTaskbar) Top = s.Top ? 0 : sh - winH;
            else { var wa = SystemParameters.WorkArea; Top = s.Top ? wa.Top : wa.Bottom - winH; }
            if (hwnd == IntPtr.Zero) return;
            if (s.Reserve && s.HideTaskbar && !AutoHiding) Taskbar.Reserve(hwnd, s.Top, (int)Math.Round(winH * scale));
            else Taskbar.Unreserve(hwnd);
            // make sure we really are where we want after appbar negotiation
            Native.SetWindowPos(hwnd, (IntPtr)(-1), (int)Math.Round(Left * scale), (int)Math.Round(Top * scale), (int)Math.Round(Width * scale), (int)Math.Round(winH * scale), 0x10);
            Dispatcher.BeginInvoke(new Action(() => { if (host != null) host.Configure(); UpdateRegion(); }), DispatcherPriority.Loaded);
        }

        public void Rebuild()
        {
            Build();
            AttachGlass();
            Layout();
            UpdateClock(); UpdateStats(); UpdatePomo(); UpdateMedia();
            UpdateStates();
            ApplyHotkey();
        }

        public void RethemePanels() { foreach (var p in Panels) p.ApplyTheme(); }

        void Build()
        {
            var s = App.S;
            S = s.IconSize; pillH = S + 22; margin = s.FullWidth ? 0 : 8; winH = pillH + margin * 2;
            radius = s.FullWidth ? 0 : pillH / 2;
            if (host != null) host.Clear();
            root.Children.Clear();
            Height = winH;

            surf = new GlassSurface(radius, s.FullWidth ? 10 : 18)
            {
                Height = pillH, VerticalAlignment = VerticalAlignment.Center, RenderTransform = SurfTransform(), RenderTransformOrigin = new Point(0.5, s.Top ? 0 : 1),
                HorizontalAlignment = s.FullWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
                Margin = new Thickness(margin, 0, margin, 0)
            };
            canvas = new Canvas();
            surf.Body.Children.Add(canvas);
            content = BuildContent();
            canvas.Children.Add(content);
            root.Children.Add(surf);
            surf.SizeChanged += (o, e) => { PlaceContent(); UpdateRegion(); };
            surf.MouseMove += OnMouseMove;
            surf.MouseEnter += (o, e) => RestartAutoHide();
            surf.MouseLeave += (o, e) => { if (dragging) return; foreach (var a in apps) if (a.View != null) a.View.SetMagnify(1); };
            surf.MouseRightButtonUp += (o, e) => { if (!e.Handled) DockMenu(); };

            int i = 0;
            foreach (var a in apps)
            {
                AddView(a, built ? 0 : i * 45);
                i++;
            }
            targetW = -1;
            if (!s.FullWidth) { surf.Width = pillH * 3; content.SizeChanged += (o, e) => OnContentSize(); }
            built = true;
        }

        TransformGroup SurfTransform() { var g = new TransformGroup(); g.Children.Add(surfScale); g.Children.Add(surfShift); return g; }

        FrameworkElement BuildContent()
        {
            var s = App.S;
            appsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(StartButton());
            left.Children.Add(Ui.Separator(S * 0.6));
            left.Children.Add(appsPanel);
            var right = BuildWidgets();
            if (s.FullWidth)
            {
                var dp = new DockPanel { Height = pillH, LastChildFill = false, Margin = new Thickness(10, 0, 10, 0) };
                DockPanel.SetDock(right, System.Windows.Controls.Dock.Right); dp.Children.Add(right);
                DockPanel.SetDock(left, System.Windows.Controls.Dock.Left); dp.Children.Add(left);
                return dp;
            }
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = pillH, Margin = new Thickness(pillH * 0.2, 0, pillH * 0.2, 0) };
            sp.Children.Add(left);
            sp.Children.Add(Ui.Separator(S * 0.6));
            sp.Children.Add(right);
            return sp;
        }

        void PlaceContent()
        {
            if (App.S.FullWidth) { content.Width = surf.ActualWidth; Canvas.SetLeft(content, 0); }
            else Canvas.SetLeft(content, Math.Round((surf.ActualWidth - content.ActualWidth - content.Margin.Left - content.Margin.Right) / 2));
            Canvas.SetTop(content, 0);
        }

        double targetW = -1;
        void OnContentSize()
        {
            if (App.S.FullWidth) return;
            double w = Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
            if (Math.Abs(w - targetW) < 0.5) return;
            bool first = targetW < 0;
            bool grow = w > targetW;
            targetW = w;
            var a = new DoubleAnimation(w, TimeSpan.FromMilliseconds(first ? 700 : 460)) { EasingFunction = new ElasticLikeEase() };
            if (first) a.From = Math.Min(w, pillH * 3);
            // keep the window only as wide as the pill: grow first, shrink after the animation
            if (grow && !first) FitWindow();
            else a.Completed += (o, e) => FitWindow();
            surf.BeginAnimation(WidthProperty, a);
            PlaceContent();
        }

        /// Pill mode: window = pill + margins (+ room for the overshoot of the spring).
        void FitWindow()
        {
            if (App.S.FullWidth || targetW <= 0) return;
            double sw = SystemParameters.PrimaryScreenWidth;
            double want = Math.Min(sw, Math.Ceiling(targetW * 1.04 + margin * 2 + 8));
            if (Math.Abs(want - Width) < 1) return;
            Left = Math.Round((sw - want) / 2);
            Width = want;
            Dispatcher.BeginInvoke(new Action(() => { if (host != null) host.Configure(); UpdateRegion(); }), DispatcherPriority.Loaded);
        }

        // ---------------------------------------------------------------- region / animation
        void UpdateRegion()
        {
            if (surf == null || hwnd == IntPtr.Zero) return;
            Glass.ClipTo(hwnd, Glass.Bounds(surf, this), radius, scale, ref lastRgn);
            if (host != null) host.UpdateAll();
        }

        void Entrance()
        {
            entrance.OnSettle = null;
            entrance.Snap(App.S.Top ? -winH : winH);
            entrance.To(0);
            if (host != null) host.Start();
            RestartAutoHide();
        }

        // fullscreen must be seen on 2 consecutive checks (~0.5 s) before anything moves: no hide/show flicker
        int fsVotes; bool fsWant;
        void FsVote(IntPtr h)
        {
            bool fs = Tracker.IsFullscreen(h);
            if (fs != fsWant) { fsWant = fs; fsVotes = 0; }
            if (fs == hiddenFs) { fsVotes = 0; return; }
            if (++fsVotes < 2) return;
            fsVotes = 0;
            EventLog.Write("fullscreen " + (fs ? "ON  " : "OFF ") + EventLog.Describe(h));
            SetFullscreen(fs);
        }

        void SetFullscreen(bool fs)
        {
            Diag.Hit("fs" + (fs ? 1 : 0));
            if (fs == hiddenFs) return;
            hiddenFs = fs;
            // fullscreen (video, game): the dock sinks away with its own animation, the island stays and
            // collapses to the PrismDock mark. Leaving fullscreen brings back time/date and the dock.
            if (App.Island != null) App.Island.SetHidden(fs);
            if (fs) { fsHid = !autoHidden; HideDock(); }
            else { if (fsHid || !AutoHiding) ShowDock(); fsHid = false; }
        }
        bool fsHid;

        static void Pulse(FrameworkElement el)
        {
            var sc = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = sc;
            var a = new DoubleAnimation(1, 1.2, TimeSpan.FromMilliseconds(260)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4), EasingFunction = Ui.InOut };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void Bench()
        {
            ShowDock();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var t = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(8) };
            t.Tick += (o, e) =>
            {
                double sec = sw.Elapsed.TotalSeconds;
                if (sec > 12) { t.Stop(); foreach (var a in apps) if (a.View != null) a.View.SetMagnify(1); Diag.Hit("benchEnd"); return; }
                if (sec < 2) return;
                double x0 = appsPanel.TranslatePoint(new Point(0, 0), surf).X, w = appsPanel.ActualWidth;
                MagnifyAt(x0 + w * (0.5 - 0.5 * Math.Cos((sec - 2) * 2.2)));
                RestartAutoHide();
            };
            t.Start();
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            MagnifyAt(e.GetPosition(surf).X);
        }

        // mouse moves arrive far more often than frames: keep only the latest position, apply once per frame
        double magX; bool magPending;
        void MagnifyAt(double mx)
        {
            if (!App.S.Magnify) return;
            if (dragging) return;
            magX = mx;
            if (magPending) return;
            magPending = true;
            Dispatcher.BeginInvoke(new Action(() => { magPending = false; ApplyMagnify(magX); }), DispatcherPriority.Render);
        }

        void ApplyMagnify(double mx)
        {
            double range = S * 2.4;
            double x0 = 0;
            try { x0 = appsPanel.TranslatePoint(new Point(0, 0), surf).X; } catch { return; }
            foreach (var a in apps)
            {
                var v = a.View;
                if (v == null || a.Removing || v.ActualWidth <= 0) continue;
                // icons are laid out side by side in appsPanel (no transforms on DockIcon itself): offset is cheap
                double cx = x0 + VisualTreeHelper.GetOffset(v).X + v.ActualWidth / 2;
                double d = Math.Abs(mx - cx);
                double m = d < range ? 1 + 0.16 * (Math.Cos(Math.PI * d / range) + 1) / 2 : 1;
                v.SetMagnify(Math.Round(m, 3));
            }
        }

        // ---------------------------------------------------------------- widgets
        FrameworkElement StartButton()
        {
            var t = Theme.Cur;
            var g = new Grid { Width = 18, Height = 18 };
            var rot = new RotateTransform(0);
            g.RenderTransform = rot; g.RenderTransformOrigin = new Point(0.5, 0.5);
            var cols = t.Tint.Length > 1 ? t.Tint : new[] { t.Fg, t.Fg };
            for (int i = 0; i < 4; i++)
            {
                var c = i < 2 ? cols[0] : cols[cols.Length - 1];
                var sq = new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(2.5),
                    Background = Ui.Br(t.Tint.Length > 1 ? Color.FromRgb(c.R, c.G, c.B) : Ui.A(t.Fg, 0.95)),
                    HorizontalAlignment = i % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = i < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom
                };
                g.Children.Add(sq);
            }
            var b = Ui.Button(g, () => OpenStart(false), "Başlat  ·  Win", S * 0.95, S * 0.95, S * 0.3);
            var spin = new Spring(0, v => rot.Angle = v, 180, 0.6);
            b.MouseEnter += (s, e) => spin.To(45);
            b.MouseLeave += (s, e) => spin.To(0);
            return b;
        }

        FrameworkElement GlyphBtn(string glyph, Action click, string tip)
        {
            return Ui.Button(Ui.Glyph(glyph, Math.Round(S * 0.38), Ui.Br(Theme.Cur.Fg)), click, tip, S * 0.82, S * 0.82, S * 0.41);
        }

        /// soft glass capsule grouping widgets (Lumabar style)
        Border Capsule(UIElement child)
        {
            var t = Theme.Cur;
            var rim = new LinearGradientBrush(Ui.A(t.Fg, t.Dark ? 0.22 : 0.5), Ui.A(t.Fg, t.Dark ? 0.06 : 0.15), 90); rim.Freeze();
            return new Border
            {
                Child = child, Height = S * 0.92, CornerRadius = new CornerRadius(S * 0.46), VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush(Ui.A(t.Fg, t.Dark ? 0.10 : 0.30), Ui.A(t.Fg, t.Dark ? 0.04 : 0.12), 90),
                BorderBrush = rim, BorderThickness = new Thickness(1), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(3, 0, 3, 0)
            };
        }

        FrameworkElement BuildWidgets()
        {
            var t = Theme.Cur; var fg = Ui.Br(t.Fg); var sub = Ui.Br(t.Sub);
            var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // ---- media: now playing + controls
            var media = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
            var info = new StackPanel { Orientation = Orientation.Horizontal };
            mediaThumb = new Border { Width = S * 0.7, Height = S * 0.7, CornerRadius = new CornerRadius(S * 0.18), Background = Ui.Br(Ui.A(t.Fg, 0.12)), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(mediaThumb);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 2, 0), MaxWidth = 150 };
            mediaTitle = Ui.Text("", 12, FontWeights.SemiBold, fg); mediaTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            mediaArtist = Ui.Text("", 10.5, FontWeights.Normal, sub); mediaArtist.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(mediaTitle); texts.Children.Add(mediaArtist);
            info.Children.Add(texts);
            var infoBtn = Ui.Button(info, ActivateMediaApp, null, 0, S * 0.82, S * 0.25);
            infoBtn.Padding = new Thickness(4, 0, 4, 0);
            infoBtn.Visibility = Visibility.Collapsed;
            mediaInfo = infoBtn;
            media.Children.Add(infoBtn);
            media.Children.Add(GlyphBtn("", Media.Prev, "Önceki"));
            playGlyph = Ui.Glyph("", Math.Round(S * 0.4), fg);
            media.Children.Add(Ui.Button(playGlyph, Media.PlayPause, "Oynat / Duraklat  ·  tekerlek: ses", S * 0.82, S * 0.82, S * 0.41));
            media.Children.Add(GlyphBtn("", Media.Next, "Sonraki"));
            media.MouseWheel += (s, e) => { Native.Keys(e.Delta > 0 ? (byte)0xAF : (byte)0xAE); e.Handled = true; };
            p.Children.Add(Capsule(media));

            p.Children.Add(GlyphBtn("", OpenSearch, "Ara  ·  Alt + Boşluk"));

            // cpu / ram mini bars
            var st = new Grid { VerticalAlignment = VerticalAlignment.Center };
            st.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); st.ColumnDefinitions.Add(new ColumnDefinition());
            st.RowDefinitions.Add(new RowDefinition()); st.RowDefinitions.Add(new RowDefinition());
            Border cb, rb;
            AddStat(st, 0, "CPU", out cb); AddStat(st, 1, "RAM", out rb);
            cpuFill = cb; ramFill = rb;
            FrameworkElement statBtn = null;
            statBtn = Ui.Button(st, () => TogglePanel(sys ?? (sys = new SystemPanel()), statBtn), "Sistem", 0, S * 0.82, S * 0.3);
            ((Border)statBtn).Padding = new Thickness(8, 0, 8, 0);
            p.Children.Add(statBtn);

            // battery
            bool ch;
            if (SysStats.Battery(out ch) >= 0)
            {
                var bp = new StackPanel { Orientation = Orientation.Horizontal };
                bp.Children.Add(Ui.Glyph("", Math.Round(S * 0.36), fg));
                batText = Ui.Text("", 11.5, FontWeights.SemiBold, fg); batText.Margin = new Thickness(4, 0, 0, 0);
                bp.Children.Add(batText);
                FrameworkElement bb = null;
                bb = Ui.Button(bp, () => TogglePanel(sys ?? (sys = new SystemPanel()), bb), "Pil", 0, S * 0.82, S * 0.3);
                ((Border)bb).Padding = new Thickness(8, 0, 8, 0);
                batWidget = bb;
                p.Children.Add(bb);
            }

            // pomodoro
            var pp = new StackPanel { Orientation = Orientation.Horizontal };
            pp.Children.Add(Ui.Glyph("", Math.Round(S * 0.38), fg));
            pomoText = Ui.Text("", 11.5, FontWeights.SemiBold, fg); pomoText.Margin = new Thickness(5, 0, 0, 0); pomoText.Visibility = Visibility.Collapsed;
            pp.Children.Add(pomoText);
            FrameworkElement pb = null;
            pb = Ui.Button(pp, () => TogglePanel(pomo ?? (pomo = new PomodoroPanel()), pb), "Odak zamanlayıcı", 0, S * 0.82, S * 0.41);
            ((Border)pb).MinWidth = S * 0.82; ((Border)pb).Padding = new Thickness(6, 0, 6, 0);
            pomoWidget = pb;
            p.Children.Add(pb);

            FrameworkElement nb = null;
            nb = GlyphBtn("", () => TogglePanel(notes ?? (notes = new NotesPanel()), nb), "Notlar");
            p.Children.Add(nb);
            FrameworkElement sb = null;
            sb = GlyphBtn("", () => TogglePanel(settingsP ?? (settingsP = new SettingsPanel()), sb), "Ayarlar");
            p.Children.Add(sb);

            // system tray: icons the user keeps visible in Windows + "^" for everything running in the background
            var tray = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            trayInline = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            tray.Children.Add(trayInline);
            FrameworkElement chev = null;
            chev = Ui.Button(Ui.Glyph(App.S.Top ? "\uE70D" : "\uE70E", Math.Round(S * 0.3), fg), () => TogglePanel(trayP ?? (trayP = new TrayPanel()), chev), "Arka planda çalışanlar", S * 0.7, S * 0.82, S * 0.35);
            tray.Children.Add(chev);
            p.Children.Add(Capsule(tray));
            FillTray();

            // clock
            var cp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            clockTime = Ui.Text("", Math.Round(S * 0.36), FontWeights.SemiBold, fg); clockTime.HorizontalAlignment = HorizontalAlignment.Center;
            clockDate = Ui.Text("", Math.Round(S * 0.26), FontWeights.Normal, sub); clockDate.HorizontalAlignment = HorizontalAlignment.Center;
            clockDate.Margin = new Thickness(0, -1, 0, 0);
            cp.Children.Add(clockTime); cp.Children.Add(clockDate);
            FrameworkElement cbn = null;
            cbn = Ui.Button(cp, () => TogglePanel(cal ?? (cal = new CalendarPanel()), cbn), null, 0, S * 0.84, S * 0.42);
            ((Border)cbn).Padding = new Thickness(12, 0, 12, 0);
            p.Children.Add(Capsule(cbn));
            return p;
        }

        void AddStat(Grid g, int row, string name, out Border fill)
        {
            var t = Theme.Cur;
            var l = Ui.Text(name, 8.5, FontWeights.Bold, Ui.Br(t.Sub)); l.Margin = new Thickness(0, 0, 5, 0);
            Grid.SetRow(l, row); g.Children.Add(l);
            var bar = Ui.Bar(34, 4, out fill); bar.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetRow(bar, row); Grid.SetColumn(bar, 1); g.Children.Add(bar);
        }

        // ---------------------------------------------------------------- tray
        void FillTray()
        {
            if (trayInline == null) return;
            trayInline.Children.Clear();
            var t = Theme.Cur;
            foreach (var i in Tray.Items.Where(x => x.Promoted).Take(6))
            {
                var ic = i;
                var img = new Image { Width = Math.Round(S * 0.42), Height = Math.Round(S * 0.42), Source = ic.Icon ?? Icons.ForPath(ic.Exe, 32) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                Border b = null;
                b = Ui.Button(img, () => Tray.Click(ic, Center(b), false, false), ic.Text.Length > 0 ? ic.Text : ic.Name, S * 0.7, S * 0.82, S * 0.35);
                b.MouseRightButtonUp += (o, e) => { Tray.Click(ic, Center(b), true, false); e.Handled = true; };
                b.MouseLeftButtonDown += (o, e) => { if (e.ClickCount == 2) Tray.Click(ic, Center(b), false, true); };
                trayInline.Children.Add(b);
            }
        }

        static Point Center(FrameworkElement el)
        {
            try { return el.PointToScreen(new Point(el.ActualWidth / 2, el.ActualHeight / 2)); } catch { return new Point(0, 0); }
        }

        // ---------------------------------------------------------------- media
        void UpdateMedia()
        {
            if (mediaInfo == null) return;
            playGlyph.Text = Media.Playing ? "" : "";
            bool show = Media.HasSession && Media.Title.Length > 0;
            var vis = show ? Visibility.Visible : Visibility.Collapsed;
            if (mediaInfo.Visibility != vis) mediaInfo.Visibility = vis;
            if (!show) return;
            mediaTitle.Text = Media.Title;
            mediaArtist.Text = Media.Artist.Length > 0 ? Media.Artist : Friendly(Media.AppId);
            mediaArtist.Visibility = mediaArtist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (Media.Thumb != null)
            {
                var ib = new ImageBrush(Media.Thumb) { Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(ib, BitmapScalingMode.HighQuality);
                mediaThumb.Background = ib;
                mediaThumb.Child = null;
            }
            else
            {
                mediaThumb.Background = Ui.Br(Ui.A(Theme.Cur.Fg, 0.12));
                mediaThumb.Child = Ui.Glyph("", Math.Round(S * 0.3), Ui.Br(Theme.Cur.Sub));
            }
            Ui.SetTip((FrameworkElement)mediaInfo, Media.Title + (Media.Artist.Length > 0 ? "\n" + Media.Artist : "") + "\n" + (Media.Playing ? "Çalıyor" : "Duraklatıldı") + " · " + Friendly(Media.AppId));
            // little "now playing" pop when the track changes
            var sc = new ScaleTransform(1, 1);
            mediaThumb.RenderTransformOrigin = new Point(0.5, 0.5);
            mediaThumb.RenderTransform = sc;
            if (lastTrack != Media.Title) { lastTrack = Media.Title; var sp = new Spring(0.7, v => { sc.ScaleX = v; sc.ScaleY = v; }, 300, 0.5); sp.To(1); }
        }
        string lastTrack;

        static string Friendly(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return "";
            string a = appId;
            int bang = a.IndexOf('!'); if (bang > 0) a = a.Substring(bang + 1);
            if (a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) a = a.Substring(0, a.Length - 4);
            if (a.Length > 0) a = char.ToUpper(a[0]) + a.Substring(1);
            return a;
        }

        void ActivateMediaApp()
        {
            string id = (Media.AppId ?? "").ToLowerInvariant();
            if (id.Length == 0) return;
            string exe = id.EndsWith(".exe") ? id : null;
            var app = apps.FirstOrDefault(a => a.Wins.Count > 0 && a.Exe != null && !a.Exe.StartsWith("hwnd:") &&
                ((exe != null && a.Exe.EndsWith("\\" + exe)) || id.Contains(Path.GetFileNameWithoutExtension(a.Exe))));
            if (app != null) Tracker.Activate(app.Wins[0].H);
        }

        // ---------------------------------------------------------------- clock / stats
        static readonly System.Globalization.CultureInfo Tr = new System.Globalization.CultureInfo("tr-TR");
        void UpdateClock()
        {
            var n = DateTime.Now;
            if (clockTime != null)
            {
                clockTime.Text = n.ToString(App.S.Seconds ? "HH:mm:ss" : "HH:mm");
                clockDate.Text = n.ToString("d MMM ddd", Tr);
            }
            clockTimer.Interval = App.S.Seconds ? TimeSpan.FromMilliseconds(1000 - n.Millisecond + 5) : TimeSpan.FromMilliseconds((60 - n.Second) * 1000 - n.Millisecond + 20);
            clockTimer.Start();
        }

        void UpdateStats()
        {
            Diag.Hit("stats");
            if (cpuFill == null || autoHidden) return;
            double c = SysStats.Cpu();
            double u, tot; double r = SysStats.Ram(out u, out tot);
            double cw = Math.Round(34 * c), rw = Math.Round(34 * r);
            if (cpuFill.Width != cw) cpuFill.Width = cw;
            if (ramFill.Width != rw) ramFill.Width = rw;
            if (batText != null && (statTicks++ % 12 == 0))
            {
                bool ch; int b = SysStats.Battery(out ch);
                batText.Text = b + "%" + (ch ? "⚡" : "");
            }
        }

        void UpdatePomo()
        {
            if (pomoText == null) return;
            bool show = Pomodoro.Running || Pomodoro.Remaining != Pomodoro.Minutes * 60;
            var vis = show ? Visibility.Visible : Visibility.Collapsed;
            pomoText.Text = Pomodoro.Text;
            if (pomoText.Visibility != vis) pomoText.Visibility = vis;
        }

        // ---------------------------------------------------------------- panels
        void TogglePanel(PanelWindow p, FrameworkElement anchor)
        {
            if (p.IsOpen) { p.CloseAnimated(); return; }
            if ((DateTime.Now - p.ClosedAt).TotalMilliseconds < 250) return;
            foreach (var o in Panels) if (o != p && o.IsOpen) o.CloseAnimated();
            RestartAutoHide();
            Point pt = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
            p.OpenAt(pt.X / scale, Edge, App.S.Top);
        }

        double Edge { get { return App.S.Top ? Top + margin + pillH : Top + margin; } }

        public void OpenSettings() { var p = settingsP ?? (settingsP = new SettingsPanel()); if (!p.IsOpen) p.OpenAt(SystemParameters.PrimaryScreenWidth / 2, Edge, App.S.Top); }

        public void OpenSearch(string q)
        {
            OpenSearch();
            if (search != null && search.IsOpen) search.DebugQuery(q);
        }

        public void OpenStart(bool fromKey)
        {
            var p = startP ?? (startP = new StartPanel());
            if (p.IsOpen) { p.CloseAnimated(); return; }
            if ((DateTime.Now - p.ClosedAt).TotalMilliseconds < 250) return;
            foreach (var o in Panels) if (o != p && o.IsOpen) o.CloseAnimated();
            p.ApplyTheme();
            p.ForceForeground = fromKey;
            p.OpenAt(SystemParameters.PrimaryScreenWidth / 2, Edge, App.S.Top);
            RestartAutoHide();
        }

        // ---------------------------------------------------------------- auto hide (shown by the island)
        bool AutoHiding { get { return App.S.Island && App.S.AutoHide; } }
        public bool DockVisible { get { return !autoHidden; } }
        public bool FullscreenActive { get { return hiddenFs; } }

        void RestartAutoHide()
        {
            autoTimer.Stop();
            if ((AutoHiding || hiddenFs) && !autoHidden) autoTimer.Start();
        }

        void AutoHideTick()
        {
            if (!(AutoHiding || hiddenFs) || autoHidden) { autoTimer.Stop(); return; }
            bool busy = IsMouseOver || Panels.Any(p => p.IsOpen);
            if (busy) { RestartAutoHide(); return; }
            HideDock();
        }

        public void RefreshFocus() { }

        public void ToggleDock() { if (autoHidden) ShowDock(); else HideDock(); }

        public void ShowDock()
        {
            if (App.Island != null) App.Island.Nudge();
            if (!autoHidden) { RestartAutoHide(); return; }
            autoHidden = false;
            Native.ShowWindow(hwnd, Native.SW_SHOWNA);
            statTimer.Start(); UpdateStats(); Tray.Pause(false);
            entrance.OnSettle = null;
            if (host != null) host.Start();
            entrance.To(0, 210, 0.74);
            RestartAutoHide();
        }

        /// own exit animation: sinks into the screen edge while shrinking and fading, then the window is hidden (0 CPU)
        public void HideDock()
        {
            if (autoHidden) return;
            autoHidden = true;
            autoTimer.Stop();
            foreach (var o in Panels) if (o.IsOpen) o.CloseAnimated();
            if (App.Island != null) App.Island.Nudge();
            foreach (var a in apps) if (a.View != null) a.View.SetMagnify(1);
            entrance.OnSettle = () => { if (autoHidden) { Native.ShowWindow(hwnd, Native.SW_HIDE); statTimer.Stop(); if (host != null) host.Stop(); Tray.Pause(true); } };
            entrance.To((App.S.Top ? -1 : 1) * (winH + 6), 140, 0.9);
        }

        public void OpenSearch()
        {
            var p = search ?? (search = new SearchWindow());
            if (p.IsOpen) { p.CloseAnimated(); return; }
            if ((DateTime.Now - p.ClosedAt).TotalMilliseconds < 250) return;
            foreach (var o in Panels) if (o != p && o.IsOpen) o.CloseAnimated();
            p.OpenCentered();
        }

        public void OpenAppPicker()
        {
            var p = picker ?? (picker = new AppPickerPanel());
            if (p.IsOpen) return;
            foreach (var o in Panels) if (o != p && o.IsOpen) o.CloseAnimated();
            p.OpenAt(SystemParameters.PrimaryScreenWidth / 2, Edge, App.S.Top);
        }

        // ---------------------------------------------------------------- apps
        void AddView(DockApp a, int delayMs)
        {
            var v = new DockIcon(a, S, pillH, host);
            a.View = v;
            v.MouseLeftButtonUp += (s, e) => { if (v.IsMouseOver && (DateTime.Now - dragEnded).TotalMilliseconds > 250) OnAppClick(a); };
            v.PreviewMouseLeftButtonDown += (s, e) => { dragV = v; dragging = false; dragStart = e.GetPosition(appsPanel); };
            v.PreviewMouseMove += (s, e) =>
            {
                if (dragV != v || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(appsPanel);
                if (!dragging && Math.Abs(p.X - dragStart.X) > 6) BeginDrag(p);
                if (dragging) DragTo(p);
            };
            v.PreviewMouseLeftButtonUp += (s, e) => { if (dragging && dragV == v) { EndDrag(); e.Handled = true; } dragV = null; };
            v.LostMouseCapture += (s, e) => { if (dragging && dragV == v) EndDrag(); };
            v.MouseRightButtonUp += (s, e) => { AppMenu(a); e.Handled = true; };
            v.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) { a.Start(); v.Bounce(); } };
            appsPanel.Children.Add(v);
            if (delayMs <= 0) v.Appear();
            else
            {
                var dt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs + 160) };
                dt.Tick += (s, e) => { dt.Stop(); v.Appear(); };
                dt.Start();
            }
            v.UpdateState(false);
        }

        // ---------------------------------------------------------------- drag to reorder
        bool dragTest;
        /// dev: drag the 2nd icon 2.5 slots right, drop, then drag it back (no real input, order not saved)
        void DragTest()
        {
            ShowDock();
            // run off-screen so nothing appears over what the user is doing
            Native.SetWindowPos(hwnd, IntPtr.Zero, (int)(Left * scale), 5000, 0, 0, 0x0001 | 0x0010 | 0x0004);
            var list = DockIcons().ToList();
            if (list.Count < 4) return;
            dragTest = true;
            DumpOrder("start");
            var v = list[1];
            double w = v.ActualWidth, sx = SlotX(v) + w / 2;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int phase = 0;
            var t = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
            t.Tick += (o, e) =>
            {
                double s = sw.Elapsed.TotalSeconds;
                if (phase == 0) { dragV = v; dragStart = new Point(sx, 10); BeginDrag(dragStart); phase = 1; }
                else if (phase == 1) { DragTo(new Point(sx + w * 2.5 * Math.Min(1, s / 1.2), 10)); if (s > 1.0 && s < 1.03) DumpOrder("dragging"); if (s > 2.2) { EndDrag(); phase = 2; Diag.Hit("dropped"); } }
                else if (phase == 2 && s > 3.3 && s < 3.33) DumpOrder("after-drop");
                else if (phase == 2 && s > 3.5) { dragV = v; dragStart = new Point(SlotX(v) + w / 2, 10); BeginDrag(dragStart); phase = 3; }
                else if (phase == 3) { double k = Math.Min(1, (s - 3.5) / 1.0); DragTo(new Point(dragStart.X - w * 2 * k, 10)); if (s > 5) { EndDrag(); phase = 4; } }
                else if (phase == 4 && s > 6.3) { DumpOrder("end"); t.Stop(); dragTest = false; Diag.Hit("dragTestEnd"); }
            };
            t.Start();
        }

        void DumpOrder(string tag)
        {
            var sb = new System.Text.StringBuilder(tag + ":");
            foreach (var i in DockIcons()) sb.Append("  " + i.App.Name + "@" + Math.Round(SlotX(i)) + (Math.Abs(i.DragT.X) > 0.5 ? "+" + Math.Round(i.DragT.X) : "") + (Panel.GetZIndex(i) > 0 ? "^" : ""));
            sb.Append("   | apps: " + string.Join(", ", apps.Select(a => a.Name)));
            try { File.AppendAllText(Path.Combine(Settings.Dir, "dragtest.log"), sb + Environment.NewLine); } catch { }
        }

        DockIcon dragV; bool dragging, dragMoved; Point dragStart; double grabOffset; DateTime dragEnded;

        static double SlotX(UIElement el) { return VisualTreeHelper.GetOffset(el).X; }
        IEnumerable<DockIcon> DockIcons() { return appsPanel.Children.OfType<DockIcon>(); }

        void BeginDrag(Point p)
        {
            dragging = true; dragMoved = false;
            grabOffset = dragStart.X - SlotX(dragV);
            foreach (var i in DockIcons()) if (i != dragV) i.SetMagnify(1);
            dragV.SetDragging(true);
            if (!dragTest) dragV.CaptureMouse();
            RestartAutoHide();
        }

        void DragTo(Point p)
        {
            double left = Math.Max(-dragV.ActualWidth * 0.5, Math.Min(appsPanel.ActualWidth - dragV.ActualWidth * 0.5, p.X - grabOffset));
            double center = left + dragV.ActualWidth / 2;
            // slot = how many other icons sit left of the pointer, measured on the layout without the dragged icon
            int idx = 0; double x = 0;
            foreach (var o in DockIcons())
            {
                if (o == dragV || o.App.Removing) continue;
                if (center > x + o.ActualWidth / 2) idx++;
                x += o.ActualWidth;
            }
            int cur = appsPanel.Children.IndexOf(dragV);
            if (idx != cur) MoveTo(idx);
            dragV.DragX(left - SlotX(dragV));
            if (host != null) host.RequestAlign();
        }

        void MoveTo(int idx)
        {
            var before = new Dictionary<DockIcon, double>();
            foreach (var i in DockIcons()) before[i] = SlotX(i);
            appsPanel.Children.Remove(dragV);
            appsPanel.Children.Insert(Math.Min(idx, appsPanel.Children.Count), dragV);
            appsPanel.UpdateLayout();
            foreach (var i in DockIcons())
                if (i != dragV) { double dx = before[i] - SlotX(i); if (Math.Abs(dx) > 0.5) i.SlideFrom(dx); }
            // keep the model in the visual order
            var order = DockIcons().Select(i => i.App).ToList();
            apps.Sort((a, b) => Rank(order, a).CompareTo(Rank(order, b)));
            dragMoved = true;
        }

        static int Rank(List<DockApp> order, DockApp a) { int i = order.IndexOf(a); return i < 0 ? int.MaxValue : i; }

        void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            var v = dragV;
            if (v.IsMouseCaptured) v.ReleaseMouseCapture();
            v.SetDragging(false);
            v.Settle();
            dragEnded = DateTime.Now;
            if (dragMoved && !dragTest)
            {
                // dropping an app somewhere pins it there; the pinned order is saved
                if (!v.App.Pinned && v.App.Launch != null && !v.App.Launch.StartsWith("hwnd:")) v.App.Pinned = true;
                App.S.Pinned = apps.Where(a => a.Pinned && a.Launch != null && !a.Removing).Select(a => a.Launch).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                App.S.Save();
            }
            RestartAutoHide();
        }

        void RemoveApp(DockApp a)
        {
            if (a.Removing) return;
            a.Removing = true;
            var v = a.View;
            if (v == null) { apps.Remove(a); return; }
            v.Disappear(() => { appsPanel.Children.Remove(v); v.Unregister(host); apps.Remove(a); });
        }

        void ScheduleRefresh() { refreshTimer.Stop(); refreshTimer.Start(); }

        void Refresh()
        {
            Diag.Hit("refresh");
            if (appsPanel == null) return;
            var wins = Tracker.Enumerate();
            foreach (var a in apps) a.Wins.Clear();
            foreach (var w in wins)
            {
                string key = w.Exe ?? ("hwnd:" + w.H.ToInt64());
                var app = apps.FirstOrDefault(x => !x.Removing && x.Exe == key);
                if (app == null)
                {
                    app = new DockApp { Exe = key, Launch = w.Exe, Name = w.Exe != null ? DockApp.Describe(w.Exe) : w.Title };
                    app.Wins.Add(w);
                    apps.Add(app);
                    AddView(app, 0);
                    continue;
                }
                app.Wins.Add(w);
            }
            foreach (var a in apps.ToList())
                if (!a.Pinned && a.Wins.Count == 0) RemoveApp(a);
            UpdateStates();
        }

        void UpdateStates()
        {
            IntPtr fg = Native.GetForegroundWindow();
            foreach (var a in apps)
                if (a.View != null && !a.Removing) a.View.UpdateState(a.Wins.Any(w => w.H == fg));
        }

        void OnAppClick(DockApp a)
        {
            var wins = a.Wins.Where(w => Native.IsWindow(w.H)).ToList();
            if (wins.Count == 0) { a.Start(); a.View.Bounce(); return; }
            IntPtr fg = Native.GetForegroundWindow();
            int idx = wins.FindIndex(w => w.H == fg);
            if (wins.Count == 1)
            {
                if (idx == 0 && !Native.IsIconic(fg)) Tracker.Minimize(fg);
                else Tracker.Activate(wins[0].H);
            }
            else Tracker.Activate(idx >= 0 ? wins[(idx + 1) % wins.Count].H : wins[0].H);
            ScheduleRefresh();
        }

        static MenuItem Item(string text, Action a)
        {
            var m = new MenuItem { Header = text, Padding = new Thickness(6, 6, 14, 6) };
            m.Click += (s, e) => a();
            return m;
        }

        void AppMenu(DockApp a)
        {
            var m = Ui.Menu();
            foreach (var w in a.Wins.Take(10))
            {
                var h = w.H;
                string title = w.Title.Length > 48 ? w.Title.Substring(0, 46) + "…" : w.Title;
                m.Items.Add(Item(title, () => Tracker.Activate(h)));
            }
            if (a.Wins.Count > 0) m.Items.Add(new Separator());
            if (a.Launch != null) m.Items.Add(Item(a.Wins.Count > 0 ? "Yeni pencere" : "Aç", () => { a.Start(); a.View.Bounce(); }));
            if (a.Launch != null)
            {
                if (a.Pinned) m.Items.Add(Item("Sabitlemeyi kaldır", () => Unpin(a)));
                else m.Items.Add(Item("Dock'a sabitle", () => Pin(a)));
            }
            if (a.Pinned) m.Items.Add(Item("Uygulama ekle…", OpenAppPicker));
            if (a.Wins.Count > 0) m.Items.Add(Item(a.Wins.Count > 1 ? "Tüm pencereleri kapat" : "Kapat", () => { foreach (var w in a.Wins) Tracker.Close(w.H); }));
            if (a.Wins.Any(w => w.Pid != 0))
            {
                m.Items.Add(new Separator());
                var kill = Item("Görevi sonlandır", () => { var wins = a.Wins.ToList(); Tracker.Kill(wins); ScheduleRefresh(); });
                kill.Foreground = Ui.Br(Color.FromRgb(255, 105, 97));
                m.Items.Add(kill);
            }
            m.IsOpen = true;
        }

        void DockMenu()
        {
            var m = Ui.Menu();
            m.Items.Add(Item("Uygulama ekle…", OpenAppPicker));
            m.Items.Add(Item("Ara…", OpenSearch));
            m.Items.Add(Item("Ayarlar", OpenSettings));
            m.Items.Add(Item("Görev Yöneticisi", () => { try { System.Diagnostics.Process.Start("taskmgr.exe"); } catch { } }));
            m.Items.Add(new Separator());
            m.Items.Add(Item("PrismDock'tan çık", App.Quit));
            m.IsOpen = true;
        }

        void Pin(DockApp a)
        {
            a.Pinned = true;
            if (!App.S.Pinned.Contains(a.Launch)) App.S.Pinned.Add(a.Launch);
            App.S.Save();
        }

        void Unpin(DockApp a)
        {
            a.Pinned = false;
            App.S.Pinned.Remove(a.Launch);
            App.S.Save();
            if (a.Wins.Count == 0) RemoveApp(a);
        }

        /// Pins a file / shortcut / exe / shell:AppsFolder item and animates it in.
        public void PinPath(string f)
        {
            if (string.IsNullOrEmpty(f) || App.S.Pinned.Contains(f)) return;
            var a = DockApp.FromPath(f);
            App.S.Pinned.Add(f);
            App.S.Save();
            var running = apps.FirstOrDefault(x => !x.Pinned && !x.Removing && a.Exe != null && x.Exe == a.Exe);
            if (running != null) { running.Pinned = true; running.Launch = f; if (running.View != null) running.View.Bounce(); return; }
            int idx = apps.FindLastIndex(x => x.Pinned) + 1;
            apps.Insert(idx, a);
            AddView(a, 0);
            appsPanel.Children.Remove(a.View);
            appsPanel.Children.Insert(Math.Min(idx, appsPanel.Children.Count), a.View);
            a.View.Bounce();
            ScheduleRefresh();
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop)) PinPath(f);
        }

        // ---------------------------------------------------------------- win32 events
        void OnWinEvent(IntPtr hook, uint ev, IntPtr h, int idObj, int idChild, uint thread, uint time)
        {
            if (ev == 0x8002)
            {
                if (idObj == 0 && App.S.HideTaskbar && Taskbar.IsTray(h))
                {
                    Native.ShowWindow(h, Native.SW_HIDE);
                    if ((DateTime.Now - lastReshowLog).TotalSeconds > 1) { lastReshowLog = DateTime.Now; EventLog.Write("taskbar re-shown by explorer -> hidden"); }
                }
                return;
            }
            Diag.Hit("winev" + ev);
            if (ev == 3)
            {
                UpdateStates();
                WatchForeground(h);
                FsVote(h);
                if (host != null) host.Kick();
            }
            else { ScheduleRefresh(); if (host != null) host.Kick(); }
        }

        /// listen to location changes of the foreground app only (cheap: one process, filtered to its windows)
        /// Fullscreen inside the same window (YouTube -> fullscreen) changes no foreground, so the foreground window
        /// is polled 4x per second: a handful of Win32 calls, far cheaper than location-change events (~200/s in a browser).
        DispatcherTimer fsPoll;
        void WatchForeground(IntPtr h)
        {
            if (fsPoll != null) return;
            fsPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            fsPoll.Tick += (s, e) => FsVote(Native.GetForegroundWindow());
            fsPoll.Start();
        }

        DateTime lastReshowLog;
        /// EVENT_OBJECT_SHOW from explorer only (its taskbar); re-attached when explorer restarts
        void HookExplorerShow()
        {
            if (hookShow != IntPtr.Zero) { Native.UnhookWinEvent(hookShow); hookShow = IntPtr.Zero; }
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;
            uint pid; Native.GetWindowThreadProcessId(tray, out pid);
            hookShow = Native.SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, winProc, pid, 0, 0);
        }

        void ScheduleFsCheck()
        {
            if (fsCheck == null)
            {
                fsCheck = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                fsCheck.Tick += (s, e) => { fsCheck.Stop(); FsVote(Native.GetForegroundWindow()); };
            }
            fsCheck.Stop(); fsCheck.Start();
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x21) { handled = true; return (IntPtr)3; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            if (msg == 0x312 && w.ToInt32() == HotkeyId) { OpenSearch(); handled = true; return IntPtr.Zero; } // WM_HOTKEY
            if (msg == (int)shellMsg && shellMsg != 0)
            {
                int code = w.ToInt32();
                Diag.Hit("shell" + code);
                if (code == 0x8006) // HSHELL_FLASH
                {
                    var app = apps.FirstOrDefault(a => a.Wins.Any(x => x.H == l));
                    if (app != null && app.View != null) app.View.Flash();
                }
                else if (code == 4 || code == 0x8004) { UpdateStates(); ScheduleRefresh(); }
                else if (code == 6) // HSHELL_REDRAW: title changed
                {
                    foreach (var a in apps)
                        foreach (var x in a.Wins)
                            if (x.H == l) { x.Title = Native.GetText(l); if (a.View != null && !a.Removing) a.View.UpdateState(a.Wins.Any(y => y.H == Native.GetForegroundWindow())); }
                }
                else if (code == 1 || code == 2) ScheduleRefresh();
            }
            else if (msg == (int)taskbarCreatedMsg && taskbarCreatedMsg != 0)
            {
                var dt = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                dt.Tick += (s, e) => { dt.Stop(); EventLog.Write("explorer restarted"); HookExplorerShow(); if (App.S.HideTaskbar) Taskbar.HideHard(App.S); Taskbar.Unreserve(hwnd); Layout(); };
                dt.Start();
            }
            else if (msg == (int)Taskbar.CallbackMsg)
            {
                if (w.ToInt32() == 2) ScheduleFsCheck();
                else if (w.ToInt32() == 1 && (DateTime.Now - lastLayout).TotalMilliseconds > 1000) { lastLayout = DateTime.Now; Layout(); }
            }
            return IntPtr.Zero;
        }
    }

    /// Smooth settle with a tiny overshoot (feels like a damped spring, finishes on time).
    class ElasticLikeEase : EasingFunctionBase
    {
        static readonly double norm = 1 - Math.Exp(-6) * Math.Cos(5);
        protected override double EaseInCore(double t)
        {
            // damped spring response 1 - e^(-6t)·cos(5t), normalised to end exactly at 1
            return (1 - Math.Exp(-6 * t) * Math.Cos(5 * t)) / norm;
        }
        protected override Freezable CreateInstanceCore() { return new ElasticLikeEase(); }
        public ElasticLikeEase() { EasingMode = EasingMode.EaseIn; }
    }
}
