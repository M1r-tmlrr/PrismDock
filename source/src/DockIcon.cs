using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PrismDock
{
    class DockApp
    {
        public string Launch;   // what we start (lnk / exe / folder / shell:AppsFolder\id)
        public string Exe;      // lowercase exe path used for window matching
        public string Name;
        public bool Pinned;
        public bool Removing;
        public List<WinInfo> Wins = new List<WinInfo>();
        public DockIcon View;

        public static bool IsShellPath(string p) { return p != null && p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase); }

        public static DockApp FromPath(string path)
        {
            var a = new DockApp { Launch = path, Pinned = true };
            if (IsShellPath(path))
            {
                a.Name = Icons.DisplayName(path) ?? path.Substring(path.LastIndexOf('\\') + 1);
                return a;
            }
            string target = Shortcut.Target(path);
            if (target != null && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) a.Exe = target.ToLowerInvariant();
            a.Name = Path.GetFileNameWithoutExtension(path);
            if (Directory.Exists(path)) a.Name = new DirectoryInfo(path).Name;
            else if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) a.Name = Describe(path);
            return a;
        }

        public static string Describe(string exe)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrEmpty(v.FileDescription)) return v.FileDescription;
            }
            catch { }
            return Path.GetFileNameWithoutExtension(exe);
        }

        /// the icon if it is already in the cache (theme change, rebuild): shown instantly, no worker round-trip
        public ImageSource CachedIcon()
        {
            var i = Icons.TryCached(Launch, 96);
            if (i == null && Exe != null && !Exe.StartsWith("hwnd:")) i = Icons.TryCached(Exe, 96);
            return i;
        }

        public ImageSource Icon()
        {
            ImageSource i = null;
            if (Launch != null) i = Icons.ForPath(Launch);
            if (i == null && Exe != null && !Exe.StartsWith("hwnd:")) i = Icons.ForPath(Exe);
            if (i == null && Wins.Count > 0) i = Icons.ForWindow(Wins[0].H);
            if (i == null) i = Icons.Letter(Name);
            return i;
        }

        public void Start()
        {
            string p = Launch ?? Exe;
            if (p == null || p.StartsWith("hwnd:")) return;
            if (p.EndsWith("Immersive Control Panel.lnk", StringComparison.OrdinalIgnoreCase)) p = "ms-settings:";
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); } catch { }
        }
    }

    /// Dock icon on a liquid-glass tile: lens-refracted backdrop, specular highlight, rim light.
    /// Magnify / press / hover are spring driven; launch bounce; attention pulse; running indicator.
    class DockIcon : Grid
    {
        public readonly DockApp App;
        readonly ScaleTransform scale = new ScaleTransform(0, 0);
        readonly TranslateTransform lift = new TranslateTransform(), bounce = new TranslateTransform();
        readonly Border indicator, glow;
        readonly SolidColorBrush glowBrush;
        readonly System.Windows.Controls.Image img;
        readonly GlassLayer tileGlass;
        readonly double size;
        readonly Spring magS, pressS, appearS, glowS, indW, shiftS;
        public readonly TranslateTransform DragT = new TranslateTransform();
        readonly BackdropHost glassHost;
        readonly bool top;

        public DockIcon(DockApp app, double iconSize, double height, BackdropHost host)
        {
            App = app; size = iconSize; glassHost = host;
            RenderTransform = DragT;
            top = PrismDock.App.S.Top;
            Width = iconSize + 12; Height = height;
            var t = Theme.Cur;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            double r = Math.Round(iconSize * 0.3);
            const double off = 2; // tile sits slightly off-center to leave room for the indicator

            var holder = new Grid
            {
                Width = iconSize, Height = iconSize, VerticalAlignment = VerticalAlignment.Center,
                Margin = top ? new Thickness(0, off * 2, 0, 0) : new Thickness(0, 0, 0, off * 2),
                RenderTransformOrigin = new Point(0.5, top ? 0.2 : 0.8)
            };
            var tg = new TransformGroup(); tg.Children.Add(scale); tg.Children.Add(lift); tg.Children.Add(bounce);
            holder.RenderTransform = tg;

            // --- glass tile ---
            tileGlass = new GlassLayer(r, iconSize * 0.2, iconSize * 0.32, 5) { AlignTo = this, IgnoreRenderTransform = true, offsetY = top ? off : -off };
            var fill = new Border { CornerRadius = new CornerRadius(r) };
            var spec = new Border { CornerRadius = new CornerRadius(r - 1, r - 1, r * 0.6, r * 0.6), VerticalAlignment = VerticalAlignment.Top, Height = iconSize * 0.48, Margin = new Thickness(1.5, 1.5, 1.5, 0) };
            var rim = new Border { CornerRadius = new CornerRadius(r), BorderThickness = new Thickness(1) };
            var shade = new Border { CornerRadius = new CornerRadius(r + 1), BorderThickness = new Thickness(1), Margin = new Thickness(-1) };
            double k = Ui.Light;
            Func<int, byte> a = v => (byte)Math.Max(0, Math.Min(255, v * k));
            Func<int, int, byte> a2 = (b0, v) => (byte)Math.Max(0, Math.Min(255, b0 + v * k));
            if (t.Dark)
            {
                fill.Background = Grad(Color.FromArgb(a2(20, 26), 255, 255, 255), Color.FromArgb(a2(6, 8), 255, 255, 255));
                spec.Background = Grad(Color.FromArgb(a(60), 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
            }
            else
            {
                fill.Background = Grad(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(90, 255, 255, 255));
                spec.Background = Grad(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
            }
            var rb = new LinearGradientBrush { StartPoint = new Point(0.2, 0), EndPoint = new Point(0.8, 1) };
            Color rc = Color.FromRgb(t.Rim1.R, t.Rim1.G, t.Rim1.B), rc2 = Color.FromRgb(t.Rim2.R, t.Rim2.G, t.Rim2.B);
            rb.GradientStops.Add(new GradientStop(Color.FromArgb(a2(40, 185), rc.R, rc.G, rc.B), 0));
            rb.GradientStops.Add(new GradientStop(Color.FromArgb(a2(15, 25), rc2.R, rc2.G, rc2.B), 0.45));
            rb.GradientStops.Add(new GradientStop(Color.FromArgb(a2(10, 20), rc2.R, rc2.G, rc2.B), 0.65));
            rb.GradientStops.Add(new GradientStop(Color.FromArgb(a2(25, 125), rc.R, rc.G, rc.B), 1));
            rb.Freeze();
            rim.BorderBrush = rb;
            shade.BorderBrush = Grad(Color.FromArgb(25, 0, 0, 0), Color.FromArgb(70, 0, 0, 0));

            glowBrush = new SolidColorBrush(t.Dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0));
            glow = new Border { CornerRadius = new CornerRadius(r), Background = glowBrush, Opacity = 0 };
            img = new System.Windows.Controls.Image { Width = Math.Round(iconSize * 0.66), Height = Math.Round(iconSize * 0.66) };
            var cached = app.CachedIcon();
            if (cached != null) img.Source = cached;
            else
            {
                Icons.Async(Dispatcher, app.Icon, i => img.Source = i);
                // never leave a tile empty: if the shell is slow, show the letter icon until the real one arrives
                var fallback = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                fallback.Tick += (s, e) => { fallback.Stop(); if (img.Source == null) img.Source = Icons.Letter(app.Name); };
                fallback.Start();
            }
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            holder.Children.Add(shade);
            holder.Children.Add(tileGlass);
            // the theme's own colouring (Gece Yarısı blue, Aurora / Gün Batımı gradients, Buz Beyazı frost ...)
            if (!t.Opaque) holder.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = Ui.Br(t.Overlay) });
            holder.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = t.TintBrush() });
            holder.Children.Add(fill);
            holder.Children.Add(spec);
            holder.Children.Add(glow);
            holder.Children.Add(img);
            holder.Children.Add(rim);
            Children.Add(holder);
            if (t.Opaque) tileGlass.Background = Ui.Br(Color.FromArgb(30, 255, 255, 255));
            else if (host != null) host.Register(tileGlass);

            indicator = new Border
            {
                Height = 4, Width = 0, CornerRadius = new CornerRadius(2), Background = Ui.Br(t.Fg), Opacity = 0,
                VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3)
            };
            Children.Add(indicator);

            magS = new Spring(1, v => Apply(), 520, 0.82);
            pressS = new Spring(1, v => Apply(), 900, 0.9);
            appearS = new Spring(0, v => Apply(), 260, 0.62);
            glowS = new Spring(0, v => glow.Opacity = Math.Max(0, v), 300, 1);
            indW = new Spring(0, v => indicator.Width = Math.Max(0, v), 380, 0.6);
            shiftS = new Spring(0, v => { DragT.X = v; if (glassHost != null) glassHost.RequestAlign(); }, 420, 0.82);

            MouseEnter += (s, e) => glowS.To(1);
            MouseLeave += (s, e) => { glowS.To(0); if (pressed) Press(false); };
            MouseLeftButtonDown += (s, e) => { Press(true); e.Handled = true; };
            MouseLeftButtonUp += (s, e) => Press(false);
        }

        static Brush Grad(Color a, Color b) { var g = new LinearGradientBrush(a, b, 90); g.Freeze(); return g; }

        void Apply()
        {
            if (magS == null || pressS == null || appearS == null) return;
            double sc = magS.Value * pressS.Value * Math.Max(0, appearS.Value);
            scale.ScaleX = sc; scale.ScaleY = sc;
            lift.Y = (top ? 1 : -1) * Math.Max(0, magS.Value - 1) * size * 0.12;
        }

        public void RefreshIcon() { img.Source = App.Icon(); }
        public void Unregister(BackdropHost host) { if (host != null) host.Unregister(tileGlass); }
        public void Attach(BackdropHost host) { if (host != null && !Theme.Cur.Opaque) host.Register(tileGlass); }

        public void Appear() { appearS.To(1); }

        /// FLIP: the icon jumped to a new slot; start where it visually was and glide into place
        public void SlideFrom(double dx) { shiftS.Snap(DragT.X + dx); shiftS.To(0); }
        /// while dragged the icon sits exactly under the pointer
        public void DragX(double x) { shiftS.Snap(x); }
        public void Settle() { shiftS.To(0, 380, 0.72); }

        public void SetDragging(bool on)
        {
            pressed = false;
            pressS.To(1, 600, 0.8);
            magS.To(on ? 1.14 : 1, 420, 0.7);
            Opacity = on ? 0.94 : 1;
            Panel.SetZIndex(this, on ? 100 : 0);
        }

        public void Disappear(Action done)
        {
            appearS.To(0, 500, 1);
            var w = new DoubleAnimation(ActualWidth, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = Ui.InOut, BeginTime = TimeSpan.FromMilliseconds(110) };
            w.Completed += (s, e) => done();
            BeginAnimation(WidthProperty, w);
        }

        bool pressed;
        public void SetMagnify(double m) { if (Math.Abs(m - magS.Target) > 0.0005) magS.To(m); }

        void Press(bool down)
        {
            pressed = down;
            if (down) pressS.To(0.84, 1100, 0.9);
            else pressS.To(1, 420, 0.42);
        }

        /// macOS style launch bounce: two decaying hops.
        public void Bounce()
        {
            double dir = top ? 1 : -1;
            var a = new DoubleAnimationUsingKeyFrames();
            var up = new SineEase { EasingMode = EasingMode.EaseOut };
            var dn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(dir * 5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170)), up));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)), dn));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(dir * 2.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450)), up));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(570)), dn));
            bounce.BeginAnimation(TranslateTransform.YProperty, a);
            pressS.Snap(0.9); pressS.To(1, 380, 0.5);
        }

        /// attention pulse when a window flashes.
        public void Flash()
        {
            var t = Theme.Cur;
            var b = new SolidColorBrush(Ui.A(t.Accent == Colors.White ? Color.FromRgb(255, 170, 60) : t.Accent, 0.55));
            glow.Background = b;
            var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), EasingFunction = Ui.InOut };
            a.Completed += (s, e) => { glow.BeginAnimation(OpacityProperty, null); glow.Background = glowBrush; glow.Opacity = glowS.Value; };
            glow.BeginAnimation(OpacityProperty, a);
            Bounce();
        }

        int lastState = -1;
        string lastTip;
        public void UpdateState(bool active)
        {
            int n = App.Wins.Count;
            int state = n == 0 ? 0 : (active ? 3 : (n > 1 ? 2 : 1));
            string tip = n == 0 ? App.Name : (n == 1 ? App.Wins[0].Title : App.Name + "  ·  " + n + " pencere");
            if (tip != lastTip) { lastTip = tip; Ui.SetTip(this, tip); }
            if (state == lastState) return;
            lastState = state;
            indW.To(state == 0 ? 0 : (state == 1 ? 4 : (state == 2 ? 10 : 16)));
            Ui.Anim(indicator, OpacityProperty, state == 0 ? 0 : (state == 3 ? 1 : 0.55), 250, Ui.Out);
            indicator.Background = Ui.Br(state == 3 ? Theme.Cur.Accent : Theme.Cur.Fg);
        }
    }
}
