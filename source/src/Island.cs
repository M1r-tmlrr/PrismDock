using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PrismDock
{
    /// Dynamic island at the top of the screen (in the same band YASB occupies): time + date only.
    /// Clicking it shows / hides the dock. Live liquid glass, spring morph animations.
    class IslandWindow : Window
    {
        IntPtr hwnd; double scale = 1;
        readonly Grid root = new Grid();
        GlassSurface surf;
        BackdropHost host;
        TextBlock time, date;
        StackPanel row;
        readonly ScaleTransform sc = new ScaleTransform(1, 1);
        readonly TranslateTransform tr = new TranslateTransform();
        Spring widthS, pressS, dropS;
        double bandTop, bandH, pillH, baseW;
        bool hover, hidden, iconMode;
        Spring modeS;
        FrameworkElement iconBox;
        readonly ScaleTransform rowSc = new ScaleTransform(1, 1), iconSc = new ScaleTransform(0.4, 0.4);
        readonly RotateTransform iconRot = new RotateTransform(-90);
        double TargetW { get { return iconMode ? Math.Round(pillH * 1.9) : baseW; } }
        Int32Rect lastRgn = new Int32Rect(-1, -1, -1, -1);
        readonly DispatcherTimer clock = new DispatcherTimer();
        static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        const double WinW = 420;

        public IslandWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = false; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            ShowActivated = false; Background = Brushes.Transparent; FontFamily = Ui.TextFont; Title = "PrismDock Island";
            Content = root;
            clock.Tick += (s, e) => Tick();
            SourceInitialized += OnSource;
            LayoutUpdated += (s, e) => UpdateRegion();
            Build();
        }

        /// band = the strip YASB's bar occupies on the primary screen (fallback: top 36 px)
        void FindBand()
        {
            bandTop = 0; bandH = 36;
            try
            {
                foreach (var p in Process.GetProcessesByName("yasb"))
                {
                    uint pid = (uint)p.Id;
                    Native.EnumWindows((h, l) =>
                    {
                        uint wp; Native.GetWindowThreadProcessId(h, out wp);
                        if (wp != pid || !Native.IsWindowVisible(h)) return true;
                        Native.RECT r; if (!Native.GetWindowRect(h, out r)) return true;
                        int sw = Native.GetSystemMetrics(0);
                        if (r.Left >= -2 && r.Left < sw / 2 && r.Right - r.Left > sw / 2 && r.Top < 60 && r.Top >= -2 && r.Bottom - r.Top < 120)
                        {
                            bandTop = Math.Max(0, r.Top) / scale; bandH = (r.Bottom - Math.Max(0, r.Top)) / scale;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                }
            }
            catch { }
        }

        void Build()
        {
            FindBand();
            baseW = 0;
            pillH = Math.Max(24, bandH - 4);
            root.Children.Clear();
            surf = new GlassSurface(pillH / 2, 10)
            {
                Height = pillH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0)
            };
            var tg = new TransformGroup(); tg.Children.Add(sc); tg.Children.Add(tr);
            surf.RenderTransform = tg;
            var t = Theme.Cur;
            row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            time = Ui.Text("", Math.Round(pillH * 0.44), FontWeights.SemiBold, Ui.Br(t.Fg));
            var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Br(Ui.A(t.Fg, 0.5)), Margin = new Thickness(9, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            date = Ui.Text("", Math.Round(pillH * 0.4), FontWeights.Normal, Ui.Br(t.Sub));
            row.Children.Add(time); row.Children.Add(dot); row.Children.Add(date);
            surf.Body.Children.Add(new Border { CornerRadius = new CornerRadius(pillH / 2), Background = Ui.Br(t.Dark ? Color.FromArgb(120, 8, 9, 12) : Color.FromArgb(150, 250, 250, 252)) });
            surf.Body.Children.Add(row);
            row.RenderTransform = rowSc; row.RenderTransformOrigin = new Point(0.5, 0.5);

            // PrismDock mark shown while an app window has focus
            var img = new Image { Width = Math.Round(pillH * 0.7), Height = Math.Round(pillH * 0.7), Source = AppIcon() };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var ig = new TransformGroup(); ig.Children.Add(iconSc); ig.Children.Add(iconRot);
            img.RenderTransform = ig; img.RenderTransformOrigin = new Point(0.5, 0.5);
            img.HorizontalAlignment = HorizontalAlignment.Center; img.VerticalAlignment = VerticalAlignment.Center;
            img.Opacity = 0; img.IsHitTestVisible = false;
            iconBox = img;
            surf.Body.Children.Add(img);
            root.Children.Add(surf);

            widthS = new Spring(pillH * 2, v => { surf.Width = Math.Max(pillH, v); }, 260, 0.7);
            modeS = new Spring(iconMode ? 1 : 0, v =>
            {
                double m = Ui.Clamp(v, 0, 1.2);
                row.Opacity = Ui.Clamp(1 - m * 1.6, 0, 1);
                rowSc.ScaleX = rowSc.ScaleY = 1 - 0.25 * Ui.Clamp(m, 0, 1);
                iconBox.Opacity = Ui.Clamp((m - 0.25) / 0.75, 0, 1);
                iconSc.ScaleX = iconSc.ScaleY = 0.4 + 0.6 * m;
                iconRot.Angle = -90 * (1 - Ui.Clamp(m, 0, 1));
            }, 190, 0.62);
            pressS = new Spring(1, v => { sc.ScaleX = v; sc.ScaleY = v; UpdateRegion(); }, 500, 0.6);
            dropS = new Spring(-pillH - 8, v => { tr.Y = v; surf.Opacity = Ui.Clamp(1 + v / (pillH + 8), 0, 1); UpdateRegion(); }, 200, 0.72);

            surf.MouseEnter += (s, e) => { hover = true; widthS.To(TargetW + 18); };
            surf.MouseLeave += (s, e) => { hover = false; widthS.To(TargetW); pressS.To(1); };
            surf.MouseLeftButtonDown += (s, e) => { pressS.To(0.93, 900, 0.9); e.Handled = true; };
            surf.MouseLeftButtonUp += (s, e) =>
            {
                pressS.To(1, 420, 0.4);
                // morph: stretch wide, then settle back (dynamic island "breath")
                widthS.Kick(900);
                if (App.Dock != null) App.Dock.ToggleDock();
                e.Handled = true;
            };
            surf.MouseRightButtonUp += (s, e) => { if (App.Dock != null) App.Dock.OpenSettings(); e.Handled = true; };

            Width = WinW; Height = bandH;
            Left = Math.Round((SystemParameters.PrimaryScreenWidth - WinW) / 2); Top = bandTop;
            Tick();
        }

        void OnSource(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            scale = Glass.Scale(this);
            Glass.SetupDwm(this, hwnd);
            Glass.ClipTo(hwnd, Rect.Empty, 0, scale, ref lastRgn);
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == 0x21) { handled = true; return (IntPtr)3; } // MA_NOACTIVATE
                if (msg == (int)tbCreated && tbCreated != 0) { Taskbar.Unreserve(hwnd); Dispatcher.BeginInvoke(new Action(Reserve), DispatcherPriority.Background); } // explorer restarted
                if (msg == (int)Taskbar.CallbackMsg && w.ToInt32() == 1 && (DateTime.Now - lastReserve).TotalMilliseconds > 1000) Dispatcher.BeginInvoke(new Action(Reserve)); // ABN_POSCHANGED
                return IntPtr.Zero;
            });
            Reserve();
            host = new BackdropHost(this, true); // frosted: text under the island (e.g. YASB) must not show through
            surf.Attach(host);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                host.Configure();
                host.Start(); dropS.To(0);
                if (App.Dock != null && App.Dock.FullscreenActive) SetHidden(true);
                var t0 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                t0.Tick += (o, e2) => { t0.Stop(); if (App.Dock != null) App.Dock.RefreshFocus(); };
                t0.Start();
            }), DispatcherPriority.Loaded);
        }

        void Tick()
        {
            var n = DateTime.Now;
            bool sec = App.S.Seconds;
            time.Text = n.ToString(sec ? "HH:mm:ss" : "HH:mm");
            date.Text = n.ToString("d MMMM dddd", Tr);
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Ceiling(row.DesiredSize.Width + pillH * 1.1);
            if (Math.Abs(w - baseW) > 0.5) { baseW = w; widthS.To(hover ? TargetW + 18 : TargetW); }
            clock.Interval = sec ? TimeSpan.FromMilliseconds(1000 - n.Millisecond + 5) : TimeSpan.FromMilliseconds((60 - n.Second) * 1000 - n.Millisecond + 20);
            clock.Start();
        }

        void UpdateRegion()
        {
            if (hwnd == IntPtr.Zero || surf == null) return;
            if (!hidden) Glass.ClipTo(hwnd, Glass.Bounds(surf, this), pillH / 2 * sc.ScaleX, scale, ref lastRgn);
            if (host != null) host.UpdateAll();
        }

        static ImageSource appIcon;
        static ImageSource AppIcon()
        {
            if (appIcon == null)
            {
                try { appIcon = Icons.ForPath(System.Reflection.Assembly.GetExecutingAssembly().Location, 64); } catch { }
                if (appIcon == null) appIcon = Icons.Letter("P");
            }
            return appIcon;
        }

        /// true: an app window has focus -> island collapses to the PrismDock mark; false: time + date
        public void SetIconMode(bool on)
        {
            if (on == iconMode || modeS == null) return;
            iconMode = on;
            modeS.To(on ? 1 : 0);
            widthS.To(hover ? TargetW + 18 : TargetW, 230, on ? 0.75 : 0.62);
            pressS.Kick(on ? -0.8 : 0.8);
        }

        /// little bounce when the dock appears / hides, so the island "answers" the click
        public void Nudge() { pressS.Kick(-1.2); }

        DateTime lastReserve;
        static readonly uint tbCreated = Native.RegisterWindowMessage("TaskbarCreated");
        /// Like YASB: the island's strip is taken out of the work area, so maximised windows (browser tabs!) start below it.
        void Reserve()
        {
            if (hwnd == IntPtr.Zero) return;
            lastReserve = DateTime.Now;
            Taskbar.Reserve(hwnd, true, (int)Math.Round((bandTop + bandH) * scale));
            Native.SetWindowPos(hwnd, (IntPtr)(-1), (int)Math.Round(Left * scale), (int)Math.Round(bandTop * scale), (int)Math.Round(WinW * scale), (int)Math.Round(bandH * scale), 0x10);
        }

        public void Release() { if (hwnd != IntPtr.Zero) Taskbar.Unreserve(hwnd); }

        public void SetHidden(bool h)
        {
            if (h == hidden || hwnd == IntPtr.Zero) return;
            hidden = h;
            if (h) { Native.ShowWindow(hwnd, Native.SW_HIDE); host.Stop(); }
            else { Native.ShowWindow(hwnd, Native.SW_SHOWNA); host.Start(); dropS.Snap(-pillH - 8); dropS.To(0); }
        }

        public void Retheme()
        {
            Build();
            if (host != null) { host.Clear(); surf.Attach(host); host.Configure(); }
            dropS.Snap(0); Tick(); widthS.Snap(TargetW);
        }
    }
}
