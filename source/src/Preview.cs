using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PrismDock
{
    /// Hover preview of an app's windows: live DWM thumbnails on glass cards, click to switch, ✕ to close.
    /// The thumbnails are composed by DWM on the GPU, so live previews cost (almost) no CPU.
    class PreviewWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)] struct PSIZE { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        struct THUMBPROPS
        {
            public int dwFlags; public Native.RECT rcDestination; public Native.RECT rcSource;
            public byte opacity; public int fVisible; public int fSourceClientAreaOnly;
        }
        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out PSIZE size);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMBPROPS p);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Native.RECT r, int size);

        class Card
        {
            public WinInfo Win;
            public IntPtr Thumb;
            public Border Root, Box;
            public double SrcW = 16, SrcH = 9;
            public Native.RECT Src;   // visible part of the window (without the invisible resize borders)
            public bool HasSrc;
            public bool Closing;
        }

        const double Pad = 14, Radius = 22;
        IntPtr hwnd; double scale = 1;
        readonly Grid root = new Grid();
        GlassSurface surf;
        StackPanel row;
        BackdropHost host;
        readonly List<Card> cards = new List<Card>();
        readonly ScaleTransform sc = new ScaleTransform(1, 1);
        readonly TranslateTransform tr = new TranslateTransform();
        Spring showS;
        Int32Rect lastRgn = new Int32Rect(-1, -1, -1, -1);
        readonly DispatcherTimer hideTimer;
        bool top, shown;
        public DockApp App { get; private set; }
        public bool IsShown { get { return shown; } }
        DateTime hiddenAt = DateTime.MaxValue;
        public bool Stale { get { return !shown && (DateTime.Now - hiddenAt).TotalSeconds > 120; } }

        public PreviewWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = false; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            ShowActivated = false; Background = Brushes.Transparent; FontFamily = Ui.TextFont; Title = "PrismDock Preview";
            SizeToContent = SizeToContent.Manual;
            Content = root;
            hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); if (!IsMouseOver) HideNow(); };
            MouseEnter += (s, e) => hideTimer.Stop();
            MouseLeave += (s, e) => HideSoon();
            LayoutUpdated += (s, e) => UpdateGeometry();
            showS = new Spring(0, v => Apply(v), 330, 0.72);
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                scale = Glass.Scale(this);
                Glass.SetupDwm(this, hwnd);
                Glass.ClipTo(hwnd, Rect.Empty, 0, scale, ref lastRgn);
                HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg == 0x21) { handled = true; return (IntPtr)3; } // never take focus
                    return IntPtr.Zero;
                });
                host = new BackdropHost(this, true);
            };
        }

        // ------------------------------------------------------------ show / hide
        public void ShowFor(DockApp app, double anchorX, double edgeY, bool top)
        {
            hideTimer.Stop();
            var wins = app.Wins.Where(w => Native.IsWindow(w.H)).ToList();
            if (wins.Count == 0) { HideNow(); return; }
            bool same = App == app && shown && wins.Select(w => w.H).SequenceEqual(cards.Where(c => !c.Closing).Select(c => c.Win.H));
            App = app; this.top = top;
            if (hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
            if (!same) Build(wins);

            // size + place: centred over the icon, kept on screen
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double pw = Math.Ceiling(surf.DesiredSize.Width + Pad * 2), ph = Math.Ceiling(surf.DesiredSize.Height + Pad * 2);
            double sw = SystemParameters.PrimaryScreenWidth;
            Width = pw; Height = ph;
            Left = Math.Round(Ui.Clamp(anchorX - pw / 2, 4, sw - pw - 4));
            Top = Math.Round(top ? edgeY + 2 : edgeY - ph - 2);
            host.Configure();

            if (!shown)
            {
                shown = true;
                showS.Snap(0);
                Show();
                Native.SetWindowPos(hwnd, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // topmost, no move/size/activate
                host.Start();
                showS.To(1, 330, 0.72);
            }
            else if (!same) { showS.Snap(0.86); showS.To(1, 420, 0.7); }   // switching apps: small pop
        }

        public void HideSoon() { if (shown) { hideTimer.Stop(); hideTimer.Start(); } }

        public void HideNow()
        {
            hideTimer.Stop();
            if (!shown) return;
            shown = false;
            showS.OnSettle = () => { if (!shown) { Hide(); ClearCards(); if (host != null) host.Stop(); App = null; hiddenAt = DateTime.Now; Memory.Soon(); } };
            showS.To(0, 600, 1);
        }

        void Apply(double p)
        {
            if (surf == null) return;
            double s = 0.9 + 0.1 * p;
            sc.ScaleX = sc.ScaleY = s;
            sc.CenterX = surf.ActualWidth / 2; sc.CenterY = top ? 0 : surf.ActualHeight;
            tr.Y = (1 - p) * (top ? -10 : 10);
            surf.Opacity = Ui.Clamp(p * 1.6, 0, 1);
            UpdateGeometry();
        }

        // ------------------------------------------------------------ content
        void Build(List<WinInfo> wins)
        {
            ClearCards();
            root.Children.Clear();
            surf = new GlassSurface(Radius, 16) { Margin = new Thickness(Pad), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var tg = new TransformGroup(); tg.Children.Add(sc); tg.Children.Add(tr);
            surf.RenderTransform = tg;
            row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            surf.Body.Children.Add(row);
            root.Children.Add(surf);
            if (host != null) { host.Clear(); surf.Attach(host); }

            int n = wins.Count;
            double tw = n <= 3 ? 220 : (n <= 5 ? 180 : 140);
            if (n * (tw + 16) + 40 > SystemParameters.PrimaryScreenWidth) tw = Math.Max(110, (SystemParameters.PrimaryScreenWidth - 60) / n - 16);
            double th = Math.Round(tw * 0.6);
            IntPtr fg = Native.GetForegroundWindow();
            foreach (var w in wins) row.Children.Add(MakeCard(w, tw, th, w.H == fg));
        }

        Border MakeCard(WinInfo w, double tw, double th, bool active)
        {
            var t = Theme.Cur;
            var c = new Card { Win = w };
            var g = new Grid { Width = tw };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid { Margin = new Thickness(2, 0, 0, 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = new Image { Width = 16, Height = 16, Source = App.View != null ? App.Icon() : null, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(ic, BitmapScalingMode.HighQuality);
            head.Children.Add(ic);
            var title = Ui.Text(string.IsNullOrEmpty(w.Title) ? App.Name : w.Title, 12, active ? FontWeights.SemiBold : FontWeights.Normal, Ui.Br(t.Fg));
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(title, 1); head.Children.Add(title);
            var x = Ui.Button(Ui.Glyph("", 10, Ui.Br(t.Fg)), () => CloseCard(c), "Pencereyi kapat", 24, 24, 12);
            x.Opacity = 0;
            Grid.SetColumn(x, 2); head.Children.Add(x);
            g.Children.Add(head);

            c.Box = new Border { Width = tw, Height = th, CornerRadius = new CornerRadius(8), Background = Ui.Br(Ui.A(t.Fg, 0.06)) };
            Grid.SetRow(c.Box, 1); g.Children.Add(c.Box);

            var hover = new SolidColorBrush(t.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(26, 0, 0, 0)) { Opacity = 0 };
            c.Root = new Border
            {
                Child = g, Padding = new Thickness(8, 6, 8, 8), Margin = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(14),
                Background = hover, Cursor = Cursors.Hand,
                BorderBrush = active ? Ui.Br(Ui.A(t.Accent == Colors.White ? Colors.White : t.Accent, 0.55)) : null, BorderThickness = new Thickness(active ? 1 : 0)
            };
            var hv = new Spring(0, v => { hover.Opacity = Math.Max(0, v); x.Opacity = Math.Max(0, v); }, 320, 1);
            c.Root.MouseEnter += (s, e) => hv.To(1);
            c.Root.MouseLeave += (s, e) => hv.To(0);
            c.Root.MouseLeftButtonUp += (s, e) => { if (e.Handled) return; HideNow(); Tracker.Activate(w.H); };
            c.Root.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Middle) { CloseCard(c); e.Handled = true; } };

            if (hwnd != IntPtr.Zero && DwmRegisterThumbnail(hwnd, w.H, out c.Thumb) == 0)
            {
                PSIZE sz;
                if (DwmQueryThumbnailSourceSize(c.Thumb, out sz) == 0 && sz.x > 0 && sz.y > 0) { c.SrcW = sz.x; c.SrcH = sz.y; }
                // crop to what is actually visible: maximised windows hang 8 px off every edge, every window has an invisible resize frame
                Native.RECT wr, vis;
                if (Native.GetWindowRect(w.H, out wr) && DwmGetWindowAttribute(w.H, 9 /*EXTENDED_FRAME_BOUNDS*/, out vis, 16) == 0)
                {
                    double kx = c.SrcW / Math.Max(1, wr.Right - wr.Left), ky = c.SrcH / Math.Max(1, wr.Bottom - wr.Top);
                    var r = new Native.RECT
                    {
                        Left = (int)Math.Round(Math.Max(0, vis.Left - wr.Left) * kx), Top = (int)Math.Round(Math.Max(0, vis.Top - wr.Top) * ky),
                        Right = (int)Math.Round(Math.Min(wr.Right - wr.Left, vis.Right - wr.Left) * kx), Bottom = (int)Math.Round(Math.Min(wr.Bottom - wr.Top, vis.Bottom - wr.Top) * ky)
                    };
                    if (r.Right - r.Left > 8 && r.Bottom - r.Top > 8) { c.Src = r; c.HasSrc = true; c.SrcW = r.Right - r.Left; c.SrcH = r.Bottom - r.Top; }
                }
            }
            cards.Add(c);
            return c.Root;
        }

        void CloseCard(Card c)
        {
            if (c.Closing) return;
            c.Closing = true;
            Tracker.Close(c.Win.H);
            if (c.Thumb != IntPtr.Zero) { DwmUnregisterThumbnail(c.Thumb); c.Thumb = IntPtr.Zero; }
            if (App != null) App.Wins.RemoveAll(w => w.H == c.Win.H);
            if (cards.All(k => k.Closing)) { HideNow(); return; }
            // the card folds away, the panel shrinks around the rest
            double w0 = c.Root.ActualWidth;
            c.Root.IsHitTestVisible = false;
            var shrink = new Spring(w0, v => { c.Root.Width = Math.Max(0, v); c.Root.Opacity = Ui.Clamp(v / Math.Max(1, w0), 0, 1); Refit(); }, 300, 0.9);
            shrink.OnSettle = () => { row.Children.Remove(c.Root); cards.Remove(c); Refit(); };
            shrink.To(0);
        }

        void Refit()
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Ceiling(surf.DesiredSize.Width + Pad * 2);
            double cx = Left + Width / 2;
            Width = w; Left = Math.Round(Ui.Clamp(cx - w / 2, 4, SystemParameters.PrimaryScreenWidth - w - 4));
            if (host != null) host.Configure();
        }

        void ClearCards()
        {
            foreach (var c in cards) if (c.Thumb != IntPtr.Zero) DwmUnregisterThumbnail(c.Thumb);
            cards.Clear();
        }

        // ------------------------------------------------------------ geometry: region + thumbnail rectangles
        void UpdateGeometry()
        {
            if (hwnd == IntPtr.Zero || surf == null) return;
            var b = shown || showS.Value > 0.001 ? Glass.Bounds(surf, this) : Rect.Empty;
            Glass.ClipTo(hwnd, b, Radius * sc.ScaleX, scale, ref lastRgn);
            if (host != null) host.UpdateAll();
            byte op = (byte)(Ui.Clamp((showS.Value - 0.35) / 0.65, 0, 1) * 255);
            foreach (var c in cards)
            {
                if (c.Thumb == IntPtr.Zero || c.Box == null) continue;
                Rect r;
                try { r = c.Box.TransformToAncestor(this).TransformBounds(new Rect(0, 0, c.Box.ActualWidth, c.Box.ActualHeight)); }
                catch { continue; }
                // fit the window's aspect ratio inside the box
                double k = Math.Min(r.Width / c.SrcW, r.Height / c.SrcH);
                double dw = c.SrcW * k, dh = c.SrcH * k;
                double x = r.X + (r.Width - dw) / 2, y = r.Y + (r.Height - dh) / 2;
                var p = new THUMBPROPS
                {
                    dwFlags = 0x1 | 0x4 | 0x8 | 0x10 | (c.HasSrc ? 0x2 : 0),
                    rcSource = c.Src,
                    rcDestination = new Native.RECT { Left = (int)Math.Round(x * scale), Top = (int)Math.Round(y * scale), Right = (int)Math.Round((x + dw) * scale), Bottom = (int)Math.Round((y + dh) * scale) },
                    opacity = c.Closing ? (byte)(op * Ui.Clamp(c.Root.Opacity, 0, 1)) : op,
                    fVisible = r.Width > 2 ? 1 : 0, fSourceClientAreaOnly = 0
                };
                DwmUpdateThumbnailProperties(c.Thumb, ref p);
            }
        }
    }
}
