using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PrismDock
{
    static class Ui
    {
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily TextFont = new FontFamily("Segoe UI Variable Display, Segoe UI");

        public static SolidColorBrush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static Color A(Color c, double a) { return Color.FromArgb((byte)(Math.Max(0, Math.Min(1, a)) * c.A), c.R, c.G, c.B); }
        /// scales alpha by f (may brighten up to opaque)
        public static Color L(Color c, double f) { return Color.FromArgb((byte)Math.Max(0, Math.Min(255, c.A * f)), c.R, c.G, c.B); }
        /// user's "glass light" setting, 1 = theme default
        public static double Light { get { return App.S != null ? App.S.GlassLight / 100.0 : 1; } }
        public static double Clamp(double v, double a, double b) { return v < a ? a : (v > b ? b : v); }

        public static IEasingFunction Out = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
        public static IEasingFunction InOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });
        public static IEasingFunction Spring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 });
        public static IEasingFunction SoftSpring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 });
        static IEasingFunction Freeze(EasingFunctionBase e) { e.Freeze(); return e; }

        public static void Anim(Animatable target, DependencyProperty dp, double to, int ms, IEasingFunction ease)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            target.BeginAnimation(dp, a);
        }
        public static void Anim(UIElement target, DependencyProperty dp, double to, int ms, IEasingFunction ease)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            target.BeginAnimation(dp, a);
        }
        public static void AnimColor(SolidColorBrush b, Color to, int ms)
        {
            b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out });
        }

        public static TextBlock Glyph(string code, double size, Brush fg)
        {
            return new TextBlock
            {
                Text = code, FontFamily = IconFont, FontSize = size, Foreground = fg,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
        }

        public static TextBlock Text(string s, double size, FontWeight w, Brush fg)
        {
            var t = new TextBlock { Text = s, FontSize = size, FontWeight = w, Foreground = fg, FontFamily = TextFont, VerticalAlignment = VerticalAlignment.Center };
            TextOptions.SetTextFormattingMode(t, TextFormattingMode.Ideal);
            return t;
        }

        /// Wraps content into a pressable, hover-highlighted rounded button driven by springs.
        public static Border Button(UIElement content, Action click, string tip, double w, double h, double radius)
        {
            var hover = new SolidColorBrush(Theme.Cur.Hover) { Opacity = 0 };
            var sc = new ScaleTransform(1, 1);
            var b = new Border
            {
                Child = content, Background = hover, CornerRadius = new CornerRadius(radius),
                RenderTransform = sc, RenderTransformOrigin = new Point(0.5, 0.5), Cursor = Cursors.Hand
            };
            if (w > 0) b.Width = w;
            if (h > 0) b.Height = h;
            if (!string.IsNullOrEmpty(tip)) SetTip(b, tip);
            var sp = new Spring(1, v => { sc.ScaleX = v; sc.ScaleY = v; }, 420, 0.65);
            var hv = new Spring(0, v => hover.Opacity = Math.Max(0, v), 300, 1.0);
            b.MouseEnter += (s, e) => { hv.To(1); sp.To(1.05, 420, 0.65); };
            b.MouseLeave += (s, e) => { hv.To(0); sp.To(1, 300, 0.7); };
            b.MouseLeftButtonDown += (s, e) => { b.CaptureMouse(); sp.To(0.9, 1100, 0.9); e.Handled = true; };
            b.MouseLeftButtonUp += (s, e) =>
            {
                bool inside = b.IsMouseCaptured && new Rect(0, 0, b.ActualWidth, b.ActualHeight).Contains(e.GetPosition(b));
                b.ReleaseMouseCapture();
                sp.To(b.IsMouseOver ? 1.05 : 1, 520, 0.45);
                if (inside && click != null) click();
                e.Handled = true;
            };
            return b;
        }

        static ControlTemplate tipTemplate, menuTemplate;
        static ControlTemplate RoundTemplate(Type t, bool items)
        {
            var ct = new ControlTemplate(t);
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(items ? 12 : 9));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var inner = new FrameworkElementFactory(items ? typeof(ItemsPresenter) : typeof(ContentPresenter));
            bd.AppendChild(inner);
            ct.VisualTree = bd;
            ct.Seal();
            return ct;
        }

        public static void SetTip(FrameworkElement el, string tip)
        {
            var t = Theme.Cur;
            if (tipTemplate == null) tipTemplate = RoundTemplate(typeof(ToolTip), false);
            var tt = new ToolTip
            {
                Content = tip, Template = tipTemplate,
                Background = Br(t.Dark ? Color.FromArgb(240, 26, 26, 30) : Color.FromArgb(245, 250, 250, 252)),
                Foreground = Br(t.Dark ? Colors.White : Color.FromRgb(20, 20, 22)), BorderBrush = Br(t.Dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0)),
                Padding = new Thickness(10, 5, 10, 6), FontFamily = TextFont, FontSize = 12, HasDropShadow = false
            };
            el.ToolTip = tt;
            ToolTipService.SetPlacement(el, App.S != null && App.S.Top ? System.Windows.Controls.Primitives.PlacementMode.Bottom : System.Windows.Controls.Primitives.PlacementMode.Top);
            ToolTipService.SetInitialShowDelay(el, 350);
            ToolTipService.SetVerticalOffset(el, App.S != null && App.S.Top ? 12 : -12);
        }

        public static ContextMenu Menu()
        {
            var t = Theme.Cur;
            if (menuTemplate == null) menuTemplate = RoundTemplate(typeof(ContextMenu), true);
            return new ContextMenu
            {
                Template = menuTemplate,
                Background = Br(t.Dark ? Color.FromArgb(246, 28, 28, 32) : Color.FromArgb(250, 248, 248, 250)),
                Foreground = Br(t.Dark ? Colors.White : Color.FromRgb(20, 20, 22)),
                BorderBrush = Br(t.Dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0)), FontFamily = TextFont, FontSize = 13, Padding = new Thickness(4, 6, 4, 6),
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint, HasDropShadow = false
            };
        }

        public static Rectangle Separator(double h)
        {
            return new Rectangle { Width = 1, Height = h, Fill = Br(A(Theme.Cur.Fg, 0.18)), Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        }

        /// iOS-like switch with spring knob.
        public static FrameworkElement Toggle(bool value, Action<bool> changed)
        {
            var t = Theme.Cur;
            bool on = value;
            var bg = new SolidColorBrush(on ? t.Accent : A(t.Fg, 0.2));
            var knobT = new TranslateTransform(on ? 18 : 0, 0);
            var knob = new Ellipse { Width = 20, Height = 20, Fill = Br(on && t.Accent == Colors.White ? Color.FromRgb(30, 30, 34) : Colors.White), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), RenderTransform = knobT };
            var track = new Border { Width = 42, Height = 24, CornerRadius = new CornerRadius(12), Background = bg, Child = knob, Cursor = Cursors.Hand };
            track.MouseLeftButtonUp += (s, e) =>
            {
                on = !on;
                AnimColor(bg, on ? t.Accent : A(t.Fg, 0.2), 200);
                knob.Fill = Br(on && t.Accent == Colors.White ? Color.FromRgb(30, 30, 34) : Colors.White);
                Anim(knobT, TranslateTransform.XProperty, on ? 18 : 0, 320, Spring);
                if (changed != null) changed(on);
            };
            return track;
        }

        public static Border Chip(string text, bool selected, Action click)
        {
            var t = Theme.Cur;
            var tb = Text(text, 12, FontWeights.SemiBold, Br(selected && t.Accent == Colors.White ? Color.FromRgb(20, 20, 24) : (selected ? Colors.White : t.Fg)));
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            var b = Button(tb, click, null, 0, 30, 15);
            b.Padding = new Thickness(12, 0, 12, 0);
            if (selected) b.Background = Br(t.Accent);
            else b.BorderBrush = Br(A(t.Fg, 0.18));
            b.BorderThickness = new Thickness(selected ? 0 : 1);
            b.Margin = new Thickness(0, 0, 6, 0);
            return b;
        }

        /// Themed slider: track + accent fill + spring knob. commit fires on release.
        public static FrameworkElement Slider(double width, double min, double max, double value, Func<double, string> fmt, Action<double> live, Action<double> commit)
        {
            var t = Theme.Cur;
            double v = Clamp(value, min, max);
            const double kh = 20;
            var g = new Grid { Width = width, Height = 28, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = Br(A(t.Fg, 0.18)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(kh / 2, 0, kh / 2, 0) };
            var fill = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = Br(t.Accent), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(kh / 2, 0, 0, 0) };
            var kt = new TranslateTransform(); var ks = new ScaleTransform(1, 1);
            var ktg = new TransformGroup(); ktg.Children.Add(ks); ktg.Children.Add(kt);
            var knob = new Ellipse
            {
                Width = kh, Height = kh, Fill = Br(Colors.White), Stroke = Br(Color.FromArgb(40, 0, 0, 0)), StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RenderTransform = ktg, RenderTransformOrigin = new Point(0.5, 0.5)
            };
            g.Children.Add(track); g.Children.Add(fill); g.Children.Add(knob);
            double span = width - kh;
            var pos = new Spring(span * (v - min) / (max - min), x => { kt.X = x; fill.Width = Math.Max(0, x); }, 600, 0.85);
            var grow = new Spring(1, x => { ks.ScaleX = x; ks.ScaleY = x; }, 500, 0.6);
            Action<Point> set = p =>
            {
                double f = Clamp((p.X - kh / 2) / span, 0, 1);
                v = min + f * (max - min);
                pos.To(f * span);
                if (live != null) live(v);
            };
            g.MouseLeftButtonDown += (s, e) => { g.CaptureMouse(); grow.To(1.2); set(e.GetPosition(g)); e.Handled = true; };
            g.MouseMove += (s, e) => { if (g.IsMouseCaptured) set(e.GetPosition(g)); };
            g.MouseLeftButtonUp += (s, e) => { if (!g.IsMouseCaptured) return; g.ReleaseMouseCapture(); grow.To(1); if (commit != null) commit(v); e.Handled = true; };
            return g;
        }

        public static Border Bar(double width, double height, out Border fill)
        {
            var t = Theme.Cur;
            fill = new Border { CornerRadius = new CornerRadius(height / 2), Background = Br(t.Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            return new Border { Width = width, Height = height, CornerRadius = new CornerRadius(height / 2), Background = Br(A(t.Fg, 0.15)), Child = fill };
        }
    }

    /// Layered liquid glass surface: live refracted backdrop + tint + light + specular sheen + rim light.
    /// Everything is drawn with anti-aliased rounded borders (no geometry clip -> no jagged corners).
    class GlassSurface : Grid
    {
        public readonly Grid Body = new Grid();
        public readonly GlassLayer Layer;
        Border overlay, tint, light, sheen, rim, innerRim, hairline;
        double radius, refract;

        public GlassSurface(double radius) : this(radius, 18) { }
        public GlassSurface(double radius, double refract)
        {
            this.refract = refract;
            Layer = new GlassLayer(radius, refract, Math.Min(radius, 24), 3);
            overlay = new Border();
            tint = new Border(); light = new Border(); sheen = new Border { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 1, 1, 0) };
            rim = new Border { BorderThickness = new Thickness(1) };
            innerRim = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(1) };
            hairline = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(-1) };
            Children.Add(hairline); Children.Add(Layer); Children.Add(overlay);
            Children.Add(tint); Children.Add(light); Children.Add(sheen); Children.Add(innerRim); Children.Add(rim); Children.Add(Body);
            SetRadius(radius);
            ApplyTheme();
            SizeChanged += (s, e) =>
            {
                sheen.Height = Math.Max(0, Math.Min(e.NewSize.Height * 0.5, 60));
                double r = Math.Min(this.radius, e.NewSize.Height / 2);
                if (r != cr.TopLeft) SetCorners(r);
            };
        }

        public void Attach(BackdropHost host) { host.Register(Layer); }
        public void UpdateBackdrop() { Layer.Align(); }

        CornerRadius cr;
        public void SetRadius(double r) { radius = r; SetCorners(r); }
        void SetCorners(double r)
        {
            cr = new CornerRadius(r);
            Layer.SetShape(r, refract, Math.Min(Math.Max(r, 12), 24), 3);
            overlay.CornerRadius = cr; tint.CornerRadius = cr; light.CornerRadius = cr; rim.CornerRadius = cr;
            innerRim.CornerRadius = new CornerRadius(Math.Max(0, r - 1));
            hairline.CornerRadius = new CornerRadius(r + 1);
            sheen.CornerRadius = new CornerRadius(Math.Max(0, r - 1), Math.Max(0, r - 1), 0, 0);
        }

        public void ApplyTheme()
        {
            var t = Theme.Cur;
            double k = Ui.Light;
            overlay.Background = t.Opaque ? null : Ui.Br(t.Overlay);
            tint.Background = t.TintBrush();
            light.Background = Grad(Ui.L(t.FillTop, 0.4 + 0.6 * k), Ui.L(t.FillBottom, 0.4 + 0.6 * k));
            sheen.Background = Grad(Ui.L(t.Sheen, k), Ui.A(t.Sheen, 0));
            var rb = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            rb.GradientStops.Add(new GradientStop(Ui.L(t.Rim1, 0.3 + 0.7 * k), 0));
            rb.GradientStops.Add(new GradientStop(Ui.L(t.Rim2, 0.3 + 0.7 * k), 0.45));
            rb.GradientStops.Add(new GradientStop(Ui.L(Ui.A(t.Rim2, 0.6), 0.3 + 0.7 * k), 0.7));
            rb.GradientStops.Add(new GradientStop(Ui.L(Ui.A(t.Rim1, 0.55), 0.3 + 0.7 * k), 1));
            rb.Freeze();
            rim.BorderBrush = rb;
            var ir = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            ir.GradientStops.Add(new GradientStop(Ui.L(Ui.A(t.Rim1, 0.25), k), 0));
            ir.GradientStops.Add(new GradientStop(Ui.A(t.Rim1, 0.0), 0.3));
            ir.GradientStops.Add(new GradientStop(Ui.A(t.Rim1, 0.0), 0.6));
            ir.GradientStops.Add(new GradientStop(Ui.L(Ui.A(t.Rim1, 0.35), k), 1));
            ir.Freeze();
            innerRim.BorderBrush = ir;
            hairline.BorderBrush = Grad(Color.FromArgb((byte)(t.Dark ? 40 : 18), 0, 0, 0), Color.FromArgb((byte)(t.Dark ? 70 : 30), 0, 0, 0));
            Layer.Rebind();
        }

        static Brush Grad(Color a, Color b)
        {
            var g = new LinearGradientBrush(a, b, new Point(0.5, 0), new Point(0.5, 1));
            g.Freeze();
            return g;
        }
    }

    /// Damped spring. All running springs are stepped together by one fixed 60 fps clock
    /// (not the render loop, which runs at the monitor rate: 165 Hz here). When nothing moves the clock sleeps.
    class Spring
    {
        public double Value, Target, Velocity;
        public double Stiffness, Damping;
        public Action OnSettle;
        readonly Action<double> apply;
        bool active;
        double eps = 0.0008;
        static readonly List<Spring> running = new List<Spring>();
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        static double lastT;
        static volatile bool hooked, posted;
        static System.Windows.Threading.Dispatcher ui;
        static System.Threading.Thread th;
        static readonly System.Threading.AutoResetEvent wake = new System.Threading.AutoResetEvent(false);
        static readonly Action frameAction = () => { posted = false; Frame(); };
        const double FrameMs = 1000.0 / 60;

        public Spring(double v, Action<double> apply, double stiffness = 320, double dampingRatio = 0.78)
        {
            Value = Target = v; this.apply = apply;
            Stiffness = stiffness; Damping = 2 * Math.Sqrt(stiffness) * dampingRatio;
            apply(v);
        }

        public void Params(double stiffness, double dampingRatio) { Stiffness = stiffness; Damping = 2 * Math.Sqrt(stiffness) * dampingRatio; }
        public void To(double t)
        {
            Target = t;
            // settle tolerance relative to the travel: sub-pixel tails of pixel springs are invisible
            eps = Math.Max(0.0008, Math.Abs(t - Value) * 0.002);
            if (active) return;
            if (Math.Abs(Value - t) > 1e-4 || Math.Abs(Velocity) > 1e-4) Activate();
            else if (OnSettle != null) OnSettle();
        }
        public void To(double t, double stiffness, double dampingRatio) { Params(stiffness, dampingRatio); To(t); }
        public void Kick(double velocity) { Velocity += velocity; eps = Math.Max(0.0008, Math.Abs(velocity) * 0.0005); Activate(); }
        public void Snap(double v) { Value = Target = v; Velocity = 0; apply(v); }

        void Activate()
        {
            if (active) return;
            active = true;
            running.Add(this);
            if (!hooked)
            {
                hooked = true;
                lastT = clock.Elapsed.TotalSeconds;
                if (ui == null) ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                if (th == null)
                {
                    th = new System.Threading.Thread(Loop) { IsBackground = true, Name = "PrismDock frame clock", Priority = System.Threading.ThreadPriority.AboveNormal };
                    th.Start();
                }
                wake.Set();
            }
        }

        static void Loop()
        {
            while (true)
            {
                if (!hooked) wake.WaitOne();
                Native.timeBeginPeriod(1);
                double next = clock.Elapsed.TotalMilliseconds;
                while (hooked)
                {
                    next += FrameMs;
                    double wait = next - clock.Elapsed.TotalMilliseconds;
                    if (wait > 0) System.Threading.Thread.Sleep((int)Math.Ceiling(wait));
                    else next = clock.Elapsed.TotalMilliseconds;   // fell behind: don't burst
                    if (!posted) { posted = true; ui.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, frameAction); }
                }
                Native.timeEndPeriod(1);
            }
        }

        static void Frame()
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(now - lastT, 1.0 / 30);
            lastT = now;
            if (dt <= 0) return;
            Diag.Hit("spring");
            for (int i = running.Count - 1; i >= 0; i--)
            {
                if (i >= running.Count) continue;
                var sp = running[i];
                int n = 4; double h = dt / n;
                for (int k = 0; k < n; k++)
                {
                    double a = -sp.Stiffness * (sp.Value - sp.Target) - sp.Damping * sp.Velocity;
                    sp.Velocity += a * h;
                    sp.Value += sp.Velocity * h;
                }
                if (Math.Abs(sp.Value - sp.Target) < sp.eps && Math.Abs(sp.Velocity) < sp.eps * 5)
                {
                    sp.Value = sp.Target; sp.Velocity = 0; sp.active = false; running.Remove(sp);
                    sp.apply(sp.Value);
                    var cb = sp.OnSettle; if (cb != null) cb();
                    continue;
                }
                sp.apply(sp.Value);
            }
            if (running.Count == 0) hooked = false;
        }
    }
}
