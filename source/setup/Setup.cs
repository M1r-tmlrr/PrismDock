using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace PrismDockSetup
{
    static class Program
    {
        public const string Version = "2.1.3";

        [STAThread]
        static int Main(string[] args)
        {
            bool un = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
            // copied as Uninstall.exe -> uninstall mode by default
            if (!un && Path.GetFileName(Assembly.GetExecutingAssembly().Location).Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase)) un = true;

            Installer.CleanTemp();
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            int si = Array.FindIndex(args, a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
            if (si >= 0 && si + 1 < args.Length) { Snapshot(args[si + 1]); return 0; }

            var w = new WizardWindow(un);
            app.Run(w);
            return 0;
        }

        static void Snapshot(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (bool un in new[] { false, true })
            {
                int n = un ? 3 : 5;
                for (int i = 0; i < n; i++)
                {
                    var wz = new Wizard(un, true);
                    wz.ShowPage(i, false);
                    Render(wz.Root, Path.Combine(dir, (un ? "uninstall_" : "install_") + i + ".png"));
                }
                var err = new Wizard(un, true);
                err.FailForSnapshot("Erişim reddedildi: C:\\Program Files\\PrismDock");
                Render(err.Root, Path.Combine(dir, (un ? "uninstall_" : "install_") + "error.png"));
            }
        }

        static void Render(FrameworkElement el, string file)
        {
            el.Measure(new Size(WizardWindow.OuterW, WizardWindow.OuterH));
            el.Arrange(new Rect(0, 0, WizardWindow.OuterW, WizardWindow.OuterH));
            el.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)WizardWindow.OuterW, (int)WizardWindow.OuterH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(el);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(file)) enc.Save(fs);
        }
    }

    // =====================================================================================
    class WizardWindow : Window
    {
        public const double W = 720, H = 460, Pad = 26;
        public const double OuterW = W + Pad * 2, OuterH = H + Pad * 2;
        readonly Wizard wz;
        bool closing;

        public WizardWindow(bool uninstall)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent; Width = OuterW; Height = OuterH;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Title = uninstall ? "PrismDock Kaldır" : "PrismDock Kurulum";
            FontFamily = G.Font;
            UseLayoutRounding = true;
            wz = new Wizard(uninstall, false);
            wz.RequestClose = AnimatedClose;
            wz.RequestDrag = () => { try { DragMove(); } catch { } };
            Content = wz.Root;
            Closing += (s, e) => { if (wz.Busy) { e.Cancel = true; return; } if (!closing) { e.Cancel = true; AnimatedClose(); } };
            Closed += (s, e) => wz.OnClosed();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && !wz.Busy) AnimatedClose(); };
            Loaded += (s, e) =>
            {
                var sc = new ScaleTransform(0.96, 0.96);
                wz.Root.RenderTransformOrigin = new Point(0.5, 0.5);
                wz.Root.RenderTransform = sc;
                wz.Root.Opacity = 0;
                G.Anim(wz.Root, OpacityProperty, 1, 260, G.Out);
                G.Anim(sc, ScaleTransform.ScaleXProperty, 1, 520, G.Spring);
                G.Anim(sc, ScaleTransform.ScaleYProperty, 1, 520, G.Spring);
                wz.ShowPage(0, true);
            };
        }

        void AnimatedClose()
        {
            if (wz.Busy || closing) return;
            closing = true;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)) { EasingFunction = G.In };
            a.Completed += (s, e) => Close();
            wz.Root.BeginAnimation(OpacityProperty, a);
            var sc = wz.Root.RenderTransform as ScaleTransform;
            if (sc != null) { G.Anim(sc, ScaleTransform.ScaleXProperty, 0.97, 170, G.In); G.Anim(sc, ScaleTransform.ScaleYProperty, 0.97, 170, G.In); }
        }
    }

    // =====================================================================================
    /// Visual helpers (palette, fonts, easing, controls).
    static class G
    {
        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly Color Teal = Color.FromRgb(94, 242, 208), Violet = Color.FromRgb(140, 90, 255), Pink = Color.FromRgb(255, 90, 170);
        public static readonly Color Fg = Colors.White, Sub = Color.FromRgb(184, 192, 221), Dim = Color.FromRgb(120, 128, 160), Red = Color.FromRgb(255, 105, 120);

        public static readonly IEasingFunction Out = Fr(new CubicEase { EasingMode = EasingMode.EaseOut });
        public static readonly IEasingFunction In = Fr(new CubicEase { EasingMode = EasingMode.EaseIn });
        public static readonly IEasingFunction InOut = Fr(new CubicEase { EasingMode = EasingMode.EaseInOut });
        public static readonly IEasingFunction Spring = Fr(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });
        static IEasingFunction Fr(EasingFunctionBase e) { e.Freeze(); return e; }

        public static SolidColorBrush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static SolidColorBrush Br(Color c, double a) { return Br(Color.FromArgb((byte)(a * 255), c.R, c.G, c.B)); }
        public static LinearGradientBrush Grad(Color a, Color b, double angle)
        {
            var g = new LinearGradientBrush(a, b, angle); g.Freeze(); return g;
        }
        public static LinearGradientBrush VGrad(Color a, Color b)
        {
            var g = new LinearGradientBrush(a, b, new Point(0.5, 0), new Point(0.5, 1)); g.Freeze(); return g;
        }
        public static Color A(Color c, double a) { return Color.FromArgb((byte)(a * 255), c.R, c.G, c.B); }

        public static void Anim(Animatable t, DependencyProperty dp, double to, int ms, IEasingFunction e)
        { t.BeginAnimation(dp, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = e }); }
        public static void Anim(UIElement t, DependencyProperty dp, double to, int ms, IEasingFunction e)
        { t.BeginAnimation(dp, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = e }); }

        public static TextBlock Text(string s, double size, FontWeight w, Color c)
        {
            var t = new TextBlock { Text = s, FontSize = size, FontWeight = w, Foreground = Br(c), FontFamily = Font, TextWrapping = TextWrapping.Wrap };
            TextOptions.SetTextFormattingMode(t, TextFormattingMode.Ideal);
            return t;
        }
        public static TextBlock Glyph(string code, double size, Brush fg)
        {
            return new TextBlock { Text = code, FontFamily = Icons, FontSize = size, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        /// pressable rounded button with hover highlight and spring scale.
        public static Border Button(UIElement content, Action click, double w, double h, double radius, Brush bg, Brush hoverBg)
        {
            var sc = new ScaleTransform(1, 1);
            var hover = new Border { CornerRadius = new CornerRadius(radius), Background = hoverBg, Opacity = 0, IsHitTestVisible = false };
            var g = new Grid(); g.Children.Add(hover); g.Children.Add(content);
            var b = new Border
            {
                Child = g, Background = bg, CornerRadius = new CornerRadius(radius), Cursor = Cursors.Hand,
                RenderTransform = sc, RenderTransformOrigin = new Point(0.5, 0.5)
            };
            if (w > 0) b.Width = w;
            if (h > 0) b.Height = h;
            b.MouseEnter += (s, e) => { if (!b.IsEnabled) return; Anim(hover, UIElement.OpacityProperty, 1, 160, Out); Anim(sc, ScaleTransform.ScaleXProperty, 1.04, 260, Spring); Anim(sc, ScaleTransform.ScaleYProperty, 1.04, 260, Spring); };
            b.MouseLeave += (s, e) => { Anim(hover, UIElement.OpacityProperty, 0, 220, Out); Anim(sc, ScaleTransform.ScaleXProperty, 1, 300, Spring); Anim(sc, ScaleTransform.ScaleYProperty, 1, 300, Spring); };
            b.MouseLeftButtonDown += (s, e) => { b.CaptureMouse(); Anim(sc, ScaleTransform.ScaleXProperty, 0.94, 90, Out); Anim(sc, ScaleTransform.ScaleYProperty, 0.94, 90, Out); e.Handled = true; };
            b.MouseLeftButtonUp += (s, e) =>
            {
                bool inside = b.IsMouseCaptured && new Rect(0, 0, b.ActualWidth, b.ActualHeight).Contains(e.GetPosition(b));
                b.ReleaseMouseCapture();
                double to = b.IsMouseOver ? 1.04 : 1;
                Anim(sc, ScaleTransform.ScaleXProperty, to, 420, Spring); Anim(sc, ScaleTransform.ScaleYProperty, to, 420, Spring);
                if (inside && click != null) click();
                e.Handled = true;
            };
            b.IsEnabledChanged += (s, e) => b.Opacity = b.IsEnabled ? 1 : 0.4;
            return b;
        }

        public static Border Primary(string text, Action click, double w)
        {
            var t = Text(text, 13.5, FontWeights.SemiBold, Colors.White);
            t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.TextWrapping = TextWrapping.NoWrap;
            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(46, 196, 172), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(112, 72, 240), 1));
            bg.Freeze();
            var b = Button(t, click, w, 38, 19, bg, VGrad(A(Colors.White, 0.22), A(Colors.White, 0.04)));
            b.BorderBrush = VGrad(A(Colors.White, 0.45), A(Colors.White, 0.08)); b.BorderThickness = new Thickness(1);
            b.Effect = new DropShadowEffect { Color = Violet, BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.45 };
            return b;
        }

        public static Border Secondary(string text, Action click, double w)
        {
            var t = Text(text, 13.5, FontWeights.SemiBold, Fg);
            t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.TextWrapping = TextWrapping.NoWrap;
            var b = Button(t, click, w, 38, 19, Br(Colors.White, 0.07), Br(Colors.White, 0.08));
            b.BorderBrush = VGrad(A(Colors.White, 0.28), A(Colors.White, 0.08)); b.BorderThickness = new Thickness(1);
            return b;
        }

        /// iOS-like switch.
        public static FrameworkElement Toggle(bool value, Action<bool> changed)
        {
            bool on = value;
            var bg = new SolidColorBrush(on ? Teal : A(Colors.White, 0.16));
            var knobT = new TranslateTransform(on ? 18 : 0, 0);
            var knob = new Ellipse
            {
                Width = 20, Height = 20, Fill = Br(on ? Color.FromRgb(12, 20, 34) : Colors.White), HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 0, 0), RenderTransform = knobT,
                Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.35 }
            };
            var track = new Border { Width = 42, Height = 24, CornerRadius = new CornerRadius(12), Background = bg, Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            track.MouseLeftButtonUp += (s, e) =>
            {
                on = !on;
                bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Teal : A(Colors.White, 0.16), TimeSpan.FromMilliseconds(200)) { EasingFunction = Out });
                knob.Fill = Br(on ? Color.FromRgb(12, 20, 34) : Colors.White);
                Anim(knobT, TranslateTransform.XProperty, on ? 18 : 0, 360, Spring);
                if (changed != null) changed(on);
            };
            return track;
        }

        /// translucent glass tile used for glyph bullets and cards.
        public static Border Tile(UIElement child, double w, double h, double r)
        {
            return new Border
            {
                Width = w, Height = h, CornerRadius = new CornerRadius(r), Child = child,
                Background = VGrad(A(Colors.White, 0.14), A(Colors.White, 0.04)),
                BorderBrush = VGrad(A(Colors.White, 0.40), A(Colors.White, 0.06)), BorderThickness = new Thickness(1)
            };
        }

        public static Border Card(UIElement child)
        {
            return new Border
            {
                Child = child, CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 6, 16, 6),
                Background = VGrad(A(Colors.White, 0.075), A(Colors.White, 0.035)),
                BorderBrush = VGrad(A(Colors.White, 0.20), A(Colors.White, 0.05)), BorderThickness = new Thickness(1)
            };
        }

        /// PrismDock logo: gradient orb with white sheen.
        public static FrameworkElement Logo(double s)
        {
            var g = new Grid { Width = s, Height = s };
            var orb = new LinearGradientBrush { StartPoint = new Point(0.1, 0.05), EndPoint = new Point(0.9, 0.95) };
            orb.GradientStops.Add(new GradientStop(Teal, 0));
            orb.GradientStops.Add(new GradientStop(Color.FromRgb(90, 150, 255), 0.5));
            orb.GradientStops.Add(new GradientStop(Violet, 1));
            g.Children.Add(new Ellipse { Fill = orb, Effect = new DropShadowEffect { Color = Violet, BlurRadius = s * 0.5, ShadowDepth = s * 0.06, Direction = 270, Opacity = 0.6 } });
            var depth = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.3), Center = new Point(0.5, 0.35), RadiusX = 0.7, RadiusY = 0.7 };
            depth.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
            depth.GradientStops.Add(new GradientStop(Color.FromArgb(90, 30, 10, 80), 1));
            g.Children.Add(new Ellipse { Fill = depth });
            g.Children.Add(new Ellipse
            {
                Fill = VGrad(A(Colors.White, 0.85), A(Colors.White, 0.05)),
                Width = s * 0.58, Height = s * 0.34, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, s * 0.1, 0, 0)
            });
            g.Children.Add(new Ellipse { Stroke = VGrad(A(Colors.White, 0.7), A(Colors.White, 0.1)), StrokeThickness = 1 });
            return g;
        }

        /// static background: deep navy -> violet with soft color blobs, rendered once.
        public static ImageSource Backdrop(int w, int h)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var basebr = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                basebr.GradientStops.Add(new GradientStop(Color.FromRgb(10, 14, 34), 0));
                basebr.GradientStops.Add(new GradientStop(Color.FromRgb(16, 20, 52), 0.5));
                basebr.GradientStops.Add(new GradientStop(Color.FromRgb(30, 16, 64), 1));
                dc.DrawRectangle(basebr, null, new Rect(0, 0, w, h));
                Blob(dc, w * 0.10, h * 0.12, w * 0.42, Teal, 0.34);
                Blob(dc, w * 0.88, h * 0.95, w * 0.50, Violet, 0.50);
                Blob(dc, w * 0.98, h * 0.05, w * 0.30, Pink, 0.20);
                Blob(dc, w * 0.40, h * 0.85, w * 0.32, Color.FromRgb(58, 123, 255), 0.22);
                // top sheen
                dc.DrawRectangle(VGrad(A(Colors.White, 0.06), A(Colors.White, 0)), null, new Rect(0, 0, w, h * 0.4));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            return rtb;
        }

        static void Blob(DrawingContext dc, double x, double y, double r, Color c, double a)
        {
            var rb = new RadialGradientBrush();
            rb.GradientStops.Add(new GradientStop(A(c, a), 0));
            rb.GradientStops.Add(new GradientStop(A(c, a * 0.45), 0.45));
            rb.GradientStops.Add(new GradientStop(A(c, 0), 1));
            rb.Freeze();
            dc.DrawEllipse(rb, null, new Point(x, y), r, r);
        }
    }

    // =====================================================================================
    /// The wizard UI. Lives in Root so it can be shown in a window or rendered off-screen.
    class Wizard
    {
        public readonly Grid Root = new Grid();
        public Action RequestClose, RequestDrag;
        public bool Busy;
        readonly bool un, snap;
        readonly Grid host = new Grid();
        readonly StackPanel steps = new StackPanel { Margin = new Thickness(0, 30, 0, 0) };
        readonly Grid nav = new Grid { Margin = new Thickness(36, 0, 28, 24), Height = 40, VerticalAlignment = VerticalAlignment.Bottom };
        Border closeBtn;
        int page = -1;
        readonly string[] stepNames;

        // state
        string installDir;
        bool optDesktop = true, optStart = true, optAuto = true, optLaunch = true, optWipe;
        TextBox dirBox; TextBlock spaceText;
        Border barFill; TextBlock statusText, pctText;
        const double BarW = 404;
        bool failed; string error;
        bool finishedOk;

        int DonePage { get { return un ? 2 : 4; } }
        int WorkPage { get { return un ? 1 : 3; } }

        public Wizard(bool uninstall, bool snapshot)
        {
            un = uninstall; snap = snapshot;
            stepNames = un ? new[] { "Onay", "Kaldırılıyor", "Tamamlandı" } : new[] { "Hoş geldin", "Konum", "Seçenekler", "Kurulum", "Tamamlandı" };
            installDir = un ? Installer.FindInstallDir() : Installer.DefaultDir;
            BuildChrome();
        }

        void BuildChrome()
        {
            double W = WizardWindow.W, H = WizardWindow.H;
            Root.Width = WizardWindow.OuterW; Root.Height = WizardWindow.OuterH;
            Root.Background = Brushes.Transparent;
            TextOptions.SetTextRenderingMode(Root, TextRenderingMode.Grayscale);

            // shadow plate (separate so the content is not rasterized through an effect)
            Root.Children.Add(new Border
            {
                Width = W, Height = H, CornerRadius = new CornerRadius(18), Background = G.Br(Color.FromRgb(8, 8, 20)),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.55, Color = Colors.Black }
            });
            var shell = new Border
            {
                Width = W, Height = H, CornerRadius = new CornerRadius(18),
                Background = new ImageBrush(G.Backdrop((int)W, (int)H)) { Stretch = Stretch.Fill }
            };
            Root.Children.Add(shell);

            var body = new Grid { Width = W, Height = H };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            body.ColumnDefinitions.Add(new ColumnDefinition());

            // ---- brand column
            var side = new Border
            {
                CornerRadius = new CornerRadius(18, 0, 0, 18),
                Background = G.VGrad(Color.FromArgb(70, 0, 0, 10), Color.FromArgb(40, 0, 0, 10)),
                BorderBrush = G.Br(Colors.White, 0.07), BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var sp = new StackPanel { Margin = new Thickness(28, 40, 20, 0) };
            var logo = G.Logo(64); logo.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(logo);
            var name = G.Text("PrismDock", 24, FontWeights.SemiBold, G.Fg); name.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(name);
            var ver = G.Text((un ? "Kaldırma aracı" : "Kurulum sihirbazı") + "  ·  v" + Program.Version, 12, FontWeights.Normal, G.Sub);
            ver.Margin = new Thickness(1, 2, 0, 0);
            sp.Children.Add(ver);
            sp.Children.Add(steps);
            side.Child = sp;
            body.Children.Add(side);

            var foot = G.Text("Liquid glass dock · Windows 10 / 11", 10.5, FontWeights.Normal, G.Dim);
            foot.VerticalAlignment = VerticalAlignment.Bottom; foot.Margin = new Thickness(28, 0, 0, 24);
            body.Children.Add(foot);

            // ---- right column
            var right = new Grid();
            Grid.SetColumn(right, 1);
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            right.RowDefinitions.Add(new RowDefinition());
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) });

            var titleBar = new Grid { Background = Brushes.Transparent };
            titleBar.MouseLeftButtonDown += (s, e) => { if (RequestDrag != null) RequestDrag(); };
            var x = G.Glyph("\uE8BB", 10, G.Br(G.Sub));
            closeBtn = G.Button(x, () => { if (RequestClose != null) RequestClose(); }, 34, 30, 10, Brushes.Transparent, G.Br(G.Red, 0.85));
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right; closeBtn.VerticalAlignment = VerticalAlignment.Top; closeBtn.Margin = new Thickness(0, 10, 12, 0);
            closeBtn.MouseEnter += (s, e) => x.Foreground = Brushes.White;
            closeBtn.MouseLeave += (s, e) => x.Foreground = G.Br(G.Sub);
            titleBar.Children.Add(closeBtn);
            right.Children.Add(titleBar);

            host.ClipToBounds = false;
            Grid.SetRow(host, 1); right.Children.Add(host);
            Grid.SetRow(nav, 2); right.Children.Add(nav);
            body.Children.Add(right);
            Root.Children.Add(body);

            // rim light on top of everything
            Root.Children.Add(new Border
            {
                Width = W, Height = H, CornerRadius = new CornerRadius(18), IsHitTestVisible = false, BorderThickness = new Thickness(1),
                BorderBrush = G.VGrad(G.A(Colors.White, 0.32), G.A(Colors.White, 0.07))
            });
        }

        // ------------------------------------------------------------------ navigation
        public void ShowPage(int i, bool animate)
        {
            if (page == 1 && !un && dirBox != null) installDir = dirBox.Text.Trim();
            int dir = i >= page ? 1 : -1;
            page = i;
            UpdateSteps();
            var el = BuildPage(i);
            var old = host.Children.Count > 0 ? host.Children[host.Children.Count - 1] as FrameworkElement : null;
            var tt = new TranslateTransform();
            el.RenderTransform = tt;
            host.Children.Add(el);
            if (animate && !snap)
            {
                el.Opacity = 0;
                tt.X = 36 * dir;
                G.Anim(tt, TranslateTransform.XProperty, 0, 380, G.Out);
                el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { EasingFunction = G.Out, BeginTime = TimeSpan.FromMilliseconds(60) });
                if (old != null)
                {
                    old.IsHitTestVisible = false;
                    var ot = old.RenderTransform as TranslateTransform;
                    if (ot != null) G.Anim(ot, TranslateTransform.XProperty, -28 * dir, 240, G.In);
                    var fa = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = G.In };
                    fa.Completed += (s, e) => host.Children.Remove(old);
                    old.BeginAnimation(UIElement.OpacityProperty, fa);
                }
            }
            else if (old != null) host.Children.Remove(old);
            BuildNav(animate && !snap);
            closeBtn.IsEnabled = i != WorkPage;
            if (i == WorkPage && !snap) StartWork();
        }

        void UpdateSteps()
        {
            steps.Children.Clear();
            for (int k = 0; k < stepNames.Length; k++)
            {
                bool done = k < page || (k == page && page == DonePage && !failed);
                bool cur = k == page;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
                var dot = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
                if (done)
                {
                    dot.Children.Add(new Ellipse { Fill = G.Grad(G.Teal, G.Violet, 45) });
                    dot.Children.Add(G.Glyph("\uE73E", 10, G.Br(Colors.White)));
                }
                else if (cur)
                {
                    dot.Children.Add(new Ellipse { Stroke = G.Grad(G.Teal, G.Violet, 45), StrokeThickness = 2, Effect = new DropShadowEffect { Color = G.Teal, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 } });
                    dot.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = G.Br(G.Teal) });
                }
                else dot.Children.Add(new Ellipse { Stroke = G.Br(Colors.White, 0.22), StrokeThickness = 1.5 });
                row.Children.Add(dot);
                var t = G.Text(stepNames[k], 13, cur ? FontWeights.SemiBold : FontWeights.Normal, cur ? G.Fg : (done ? G.Sub : G.Dim));
                t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(t);
                steps.Children.Add(row);
            }
        }

        void BuildNav(bool animate)
        {
            nav.Children.Clear();
            var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            var rightP = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            nav.Children.Add(left); nav.Children.Add(rightP);
            Action<Border> addR = b => { b.Margin = new Thickness(10, 0, 0, 0); rightP.Children.Add(b); };

            if (page == DonePage)
            {
                if (failed && !un) addR(G.Secondary("Geri", () => { failed = false; ShowPage(1, true); }, 96));
                addR(G.Primary(failed ? "Kapat" : "Bitir", Finish, 120));
            }
            else if (page == WorkPage) { /* no navigation while working */ }
            else if (un)
            {
                left.Children.Add(G.Secondary("İptal", () => { if (RequestClose != null) RequestClose(); }, 96));
                addR(G.Primary("Kaldır", () => ShowPage(1, true), 120));
            }
            else
            {
                left.Children.Add(G.Secondary("İptal", () => { if (RequestClose != null) RequestClose(); }, 96));
                if (page > 0) addR(G.Secondary("Geri", () => ShowPage(page - 1, true), 96));
                if (page == 1) addR(G.Primary("İleri", () => { if (ValidateDir()) ShowPage(2, true); }, 120));
                else if (page == 2) addR(G.Primary("Kur", () => ShowPage(3, true), 120));
                else addR(G.Primary("İleri", () => ShowPage(page + 1, true), 120));
            }
            if (animate)
            {
                nav.Opacity = 0;
                nav.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(80), EasingFunction = G.Out });
            }
        }

        // ------------------------------------------------------------------ pages
        FrameworkElement BuildPage(int i)
        {
            if (un)
            {
                if (i == 0) return PageUninstallConfirm();
                if (i == 1) return PageWork("PrismDock kaldırılıyor", "Lütfen bekle, bu birkaç saniye sürer.");
                return PageDone();
            }
            switch (i)
            {
                case 0: return PageWelcome();
                case 1: return PageLocation();
                case 2: return PageOptions();
                case 3: return PageWork("PrismDock kuruluyor", "Dosyalar kopyalanıyor ve kısayollar hazırlanıyor.");
                default: return PageDone();
            }
        }

        static StackPanel PageFrame(string title, string sub)
        {
            var sp = new StackPanel { Margin = new Thickness(36, 6, 36, 0) };
            sp.Children.Add(G.Text(title, 25, FontWeights.SemiBold, G.Fg));
            var s = G.Text(sub, 13.5, FontWeights.Normal, G.Sub);
            s.Margin = new Thickness(0, 6, 0, 22); s.LineHeight = 20;
            sp.Children.Add(s);
            return sp;
        }

        static FrameworkElement Feature(string glyph, string title, string sub, Color c)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var tile = G.Tile(G.Glyph(glyph, 15, G.Br(c)), 36, 36, 11);
            tile.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(tile);
            var st = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(G.Text(title, 13.5, FontWeights.SemiBold, G.Fg));
            var s2 = G.Text(sub, 12, FontWeights.Normal, G.Sub); s2.Margin = new Thickness(0, 1, 0, 0);
            st.Children.Add(s2);
            Grid.SetColumn(st, 1); g.Children.Add(st);
            return g;
        }

        FrameworkElement PageWelcome()
        {
            var sp = PageFrame("PrismDock'a hoş geldin", "Bu sihirbaz PrismDock'u bilgisayarına kuracak. Yönetici izni gerekmez, yalnızca senin hesabına kurulur.");
            sp.Children.Add(Feature("\uE790", "Liquid Glass görünüm", "Canlı cam efekti, 8 tema, akıcı yay animasyonları", G.Teal));
            sp.Children.Add(Feature("\uE71D", "Uygulamalar ve pencereler", "Sabitlenen uygulamalar, gruplanan açık pencereler", Color.FromRgb(140, 180, 255)));
            sp.Children.Add(Feature("\uE8D6", "Widget'lar", "Medya, dosya arama, sistem, takvim, notlar, odak zamanlayıcı", Color.FromRgb(196, 150, 255)));
            sp.Children.Add(Feature("\uE945", "Hafif", "Düşük bellek ve boştayken neredeyse sıfır işlemci", Color.FromRgb(255, 170, 210)));
            return sp;
        }

        /// real build number (Environment.OSVersion is capped by the app manifest)
        static bool IsWindows11
        {
            get
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        int b; return k != null && int.TryParse(k.GetValue("CurrentBuildNumber") as string, out b) && b >= 22000;
                    }
                }
                catch { return false; }
            }
        }

        FrameworkElement PageLocation()
        {
            var sp = PageFrame("Kurulum konumu", "PrismDock aşağıdaki klasöre kurulacak. Farklı bir klasör seçmek için Gözat'a tıkla.");
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dirBox = new TextBox
            {
                Text = installDir, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = G.Br(G.Fg),
                CaretBrush = G.Br(G.Teal), SelectionBrush = G.Br(G.Violet), FontSize = 13, FontFamily = G.Font,
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0)
            };
            dirBox.TextChanged += (s, e) => UpdateSpace();
            var ic = G.Glyph("\uE8B7", 15, G.Br(G.Teal)); ic.Margin = new Thickness(0, 0, 10, 0);
            var inner = new DockPanel(); DockPanel.SetDock(ic, Dock.Left); inner.Children.Add(ic); inner.Children.Add(dirBox);
            var box = new Border
            {
                Height = 40, CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 0, 12, 0), Child = inner,
                Background = G.Br(Color.FromArgb(90, 0, 0, 12)), BorderBrush = G.VGrad(G.A(Colors.White, 0.08), G.A(Colors.White, 0.22)), BorderThickness = new Thickness(1)
            };
            dirBox.GotKeyboardFocus += (s, e) => box.BorderBrush = G.Grad(G.Teal, G.Violet, 0);
            dirBox.LostKeyboardFocus += (s, e) => box.BorderBrush = G.VGrad(G.A(Colors.White, 0.08), G.A(Colors.White, 0.22));
            row.Children.Add(box);
            var browse = G.Secondary("Gözat…", Browse, 92); browse.Height = 40; browse.CornerRadius = new CornerRadius(11);
            browse.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(browse, 1); row.Children.Add(browse);
            sp.Children.Add(row);

            spaceText = G.Text("", 12, FontWeights.Normal, G.Sub); spaceText.Margin = new Thickness(2, 12, 0, 0);
            sp.Children.Add(spaceText);
            UpdateSpace();

            var note = G.Card(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { G.Glyph("\uE946", 14, G.Br(G.Teal)), WithMargin(G.Text("Ayarların ve notların %AppData%\\PrismDock klasöründe saklanır; kurulum klasöründen bağımsızdır.", 12, FontWeights.Normal, G.Sub), 10, 280) }
            });
            note.Margin = new Thickness(0, 22, 0, 0); note.Padding = new Thickness(14, 10, 14, 10);
            sp.Children.Add(note);
            return sp;
        }

        static FrameworkElement WithMargin(TextBlock t, double left, double maxW)
        {
            t.Margin = new Thickness(left, 0, 0, 0); t.MaxWidth = maxW + 100; t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        void UpdateSpace()
        {
            if (spaceText == null) return;
            string d = dirBox != null ? dirBox.Text.Trim() : installDir;
            string need = (Installer.PayloadSize / 1048576.0 * 1.1 + 0.05).ToString("0.0") + " MB";
            string free = "";
            try
            {
                if (Path.IsPathRooted(d))
                {
                    var di = new DriveInfo(Path.GetPathRoot(d));
                    free = "   ·   Boş alan: " + (di.AvailableFreeSpace / 1073741824.0).ToString("0.0") + " GB";
                }
            }
            catch { }
            spaceText.Foreground = G.Br(G.Sub);
            spaceText.Text = "Gereken alan: " + need + free;
        }

        bool ValidateDir()
        {
            string d = dirBox != null ? dirBox.Text.Trim() : installDir;
            bool ok = false;
            try { ok = d.Length > 3 && Path.IsPathRooted(d) && d.IndexOfAny(Path.GetInvalidPathChars()) < 0 && !string.IsNullOrEmpty(Path.GetFileName(d.TrimEnd('\\'))); } catch { }
            if (!ok && spaceText != null)
            {
                spaceText.Foreground = G.Br(G.Red);
                spaceText.Text = "Geçerli bir klasör yolu gir (ör. C:\\Uygulamalar\\PrismDock).";
                return false;
            }
            installDir = d;
            return true;
        }

        void Browse()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "PrismDock'un kurulacağı klasörü seç", ShowNewFolderButton = true })
            {
                try { var cur = dirBox.Text.Trim(); var p = Directory.Exists(cur) ? cur : Path.GetDirectoryName(cur); if (Directory.Exists(p)) dlg.SelectedPath = p; } catch { }
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                string sel = dlg.SelectedPath;
                if (!Path.GetFileName(sel.TrimEnd('\\')).Equals("PrismDock", StringComparison.OrdinalIgnoreCase)) sel = Path.Combine(sel, "PrismDock");
                dirBox.Text = sel;
            }
        }

        FrameworkElement OptionRow(string glyph, string title, string sub, bool v, Action<bool> set, bool last)
        {
            var g = new Grid { Margin = new Thickness(0, 9, 0, 9) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = G.Glyph(glyph, 16, G.Br(G.Teal)); ic.Width = 22;
            g.Children.Add(ic);
            var st = new StackPanel { Margin = new Thickness(14, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(G.Text(title, 13.5, FontWeights.SemiBold, G.Fg));
            var s2 = G.Text(sub, 11.5, FontWeights.Normal, G.Sub); st.Children.Add(s2);
            Grid.SetColumn(st, 1); g.Children.Add(st);
            var tg = G.Toggle(v, set); Grid.SetColumn(tg, 2); g.Children.Add(tg);
            if (last) return g;
            var wrap = new StackPanel();
            wrap.Children.Add(g);
            wrap.Children.Add(new Rectangle { Height = 1, Fill = G.Br(Colors.White, 0.07), Margin = new Thickness(36, 0, 0, 0) });
            return wrap;
        }

        FrameworkElement PageOptions()
        {
            var sp = PageFrame("Seçenekler", "Kısayolları ve başlangıç davranışını seç. Bunları daha sonra PrismDock ayarlarından da değiştirebilirsin.");
            sp.Children[1].SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 16));
            var list = new StackPanel();
            list.Children.Add(OptionRow("\uE7F4", "Masaüstü kısayolu", "Masaüstüne PrismDock simgesi ekle", optDesktop, v => optDesktop = v, false));
            list.Children.Add(OptionRow("\uE8FC", "Başlat menüsü kısayolu", "Başlat menüsünde ve aramada görünsün", optStart, v => optStart = v, false));
            list.Children.Add(OptionRow("\uE7E8", "Windows ile başlat", "Oturum açınca PrismDock otomatik açılsın", optAuto, v => optAuto = v, false));
            list.Children.Add(OptionRow("\uE768", "Kurulum bitince başlat", "Sihirbaz kapanınca PrismDock hemen açılsın", optLaunch, v => optLaunch = v, true));
            sp.Children.Add(G.Card(list));
            return sp;
        }

        FrameworkElement PageWork(string title, string sub)
        {
            var sp = PageFrame(title, sub);
            sp.Margin = new Thickness(36, 6, 36, 0);
            var ring = Spinner();
            ring.HorizontalAlignment = HorizontalAlignment.Left; ring.Margin = new Thickness(0, 10, 0, 26);
            sp.Children.Add(ring);

            var head = new Grid { Width = BarW, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            statusText = G.Text(snap ? "Dosyalar kopyalanıyor…" : "Hazırlanıyor…", 13, FontWeights.Normal, G.Sub);
            pctText = G.Text(snap ? "62%" : "0%", 13, FontWeights.SemiBold, G.Fg); pctText.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(statusText); head.Children.Add(pctText);
            sp.Children.Add(head);

            var shimmer = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), SpreadMethod = GradientSpreadMethod.Repeat };
            shimmer.GradientStops.Add(new GradientStop(G.Teal, 0));
            shimmer.GradientStops.Add(new GradientStop(Color.FromRgb(120, 140, 255), 0.35));
            shimmer.GradientStops.Add(new GradientStop(Color.FromRgb(220, 210, 255), 0.5));
            shimmer.GradientStops.Add(new GradientStop(Color.FromRgb(120, 140, 255), 0.65));
            shimmer.GradientStops.Add(new GradientStop(G.Teal, 1));
            var shift = new TranslateTransform();
            shimmer.RelativeTransform = shift;
            if (!snap) shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1600)) { RepeatBehavior = RepeatBehavior.Forever });
            barFill = new Border
            {
                CornerRadius = new CornerRadius(5), Background = shimmer, HorizontalAlignment = HorizontalAlignment.Left, Width = snap ? BarW * 0.62 : 10,
                Effect = new DropShadowEffect { Color = G.Teal, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.55 }
            };
            var track = new Border
            {
                Width = BarW, Height = 10, CornerRadius = new CornerRadius(5), Background = G.Br(Color.FromArgb(110, 0, 0, 14)),
                BorderBrush = G.VGrad(G.A(Colors.White, 0.05), G.A(Colors.White, 0.16)), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left, Child = barFill, Padding = new Thickness(0)
            };
            sp.Children.Add(track);
            return sp;
        }

        FrameworkElement Spinner()
        {
            var g = new Grid { Width = 54, Height = 54 };
            g.Children.Add(new Ellipse { Stroke = G.Br(Colors.White, 0.08), StrokeThickness = 4 });
            var arc = new System.Windows.Shapes.Path
            {
                Stroke = G.Grad(G.Teal, G.Violet, 0), StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse("M 27,2 A 25,25 0 0 1 52,27"), RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var rot = new RotateTransform(snap ? 30 : 0);
            arc.RenderTransform = rot;
            if (!snap) rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(950)) { RepeatBehavior = RepeatBehavior.Forever });
            g.Children.Add(arc);
            var logo = G.Logo(26); g.Children.Add(logo);
            return g;
        }

        FrameworkElement PageDone()
        {
            string title, sub;
            if (failed) { title = un ? "Kaldırma tamamlanamadı" : "Kurulum tamamlanamadı"; sub = "Bir sorun oluştu: " + error; }
            else if (un) { title = "PrismDock kaldırıldı"; sub = "Windows görev çubuğu geri getirildi. Kalan dosyalar bu pencereyi kapatınca silinecek."; }
            else { title = "Kurulum tamamlandı"; sub = "PrismDock kullanıma hazır. " + (optLaunch ? "Bitir'e tıkladığında dock açılacak." : "Başlat menüsünden ya da masaüstü kısayolundan açabilirsin."); }

            var sp = new StackPanel { Margin = new Thickness(36, 6, 36, 0) };
            var badge = new Grid { Width = 76, Height = 76, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 22), RenderTransformOrigin = new Point(0.5, 0.5) };
            Color c1 = failed ? G.Red : G.Teal, c2 = failed ? Color.FromRgb(200, 60, 120) : G.Violet;
            badge.Children.Add(new Ellipse { Fill = G.Grad(c1, c2, 45), Effect = new DropShadowEffect { Color = c2, BlurRadius = 30, ShadowDepth = 4, Direction = 270, Opacity = 0.6 } });
            badge.Children.Add(new Ellipse { Fill = G.VGrad(G.A(Colors.White, 0.5), G.A(Colors.White, 0)), Width = 50, Height = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) });
            badge.Children.Add(new Ellipse { Stroke = G.VGrad(G.A(Colors.White, 0.7), G.A(Colors.White, 0.1)), StrokeThickness = 1 });
            var glyph = G.Glyph(failed ? "\uE711" : "\uE73E", 30, G.Br(Colors.White));
            badge.Children.Add(glyph);
            if (!snap)
            {
                var sc = new ScaleTransform(0.3, 0.3); badge.RenderTransform = sc;
                G.Anim(sc, ScaleTransform.ScaleXProperty, 1, 620, G.Spring); G.Anim(sc, ScaleTransform.ScaleYProperty, 1, 620, G.Spring);
                var gr = new RotateTransform(-40); glyph.RenderTransformOrigin = new Point(0.5, 0.5); glyph.RenderTransform = gr;
                gr.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(700)) { EasingFunction = G.Spring, BeginTime = TimeSpan.FromMilliseconds(120) });
            }
            sp.Children.Add(badge);
            sp.Children.Add(G.Text(title, 25, FontWeights.SemiBold, G.Fg));
            var s = G.Text(sub, 13.5, FontWeights.Normal, failed ? Color.FromRgb(255, 190, 200) : G.Sub); s.Margin = new Thickness(0, 6, 0, 0); s.LineHeight = 20;
            sp.Children.Add(s);
            if (!failed && !un)
            {
                var hint = G.Card(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { G.Glyph("\uE946", 14, G.Br(G.Teal)), WithMargin(G.Text("Kaldırmak için: Ayarlar › Uygulamalar › PrismDock.", 12, FontWeights.Normal, G.Sub), 10, 280) }
                });
                hint.Margin = new Thickness(0, 22, 0, 0); hint.Padding = new Thickness(14, 10, 14, 10);
                sp.Children.Add(hint);
            }
            return sp;
        }

        FrameworkElement PageUninstallConfirm()
        {
            var sp = PageFrame("PrismDock kaldırılsın mı?", "PrismDock ve kısayolları bilgisayarından silinecek. Windows görev çubuğu otomatik olarak geri getirilir.");
            var loc = G.Card(new StackPanel
            {
                Children =
                {
                    G.Text("KURULUM KLASÖRÜ", 10.5, FontWeights.SemiBold, G.Dim),
                    WithMargin(G.Text(installDir ?? "(bulunamadı)", 13, FontWeights.Normal, G.Fg), 0, 300)
                }
            });
            loc.Padding = new Thickness(16, 12, 16, 12);
            sp.Children.Add(loc);

            var g = new Grid { Margin = new Thickness(2, 20, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var st = new StackPanel();
            st.Children.Add(G.Text("Ayarları ve notları da sil", 13.5, FontWeights.SemiBold, G.Fg));
            st.Children.Add(G.Text("%AppData%\\PrismDock (tema, sabitlenen uygulamalar, notlar)", 11.5, FontWeights.Normal, G.Sub));
            g.Children.Add(st);
            var tg = G.Toggle(optWipe, v => optWipe = v); Grid.SetColumn(tg, 1); g.Children.Add(tg);
            sp.Children.Add(g);
            return sp;
        }

        // ------------------------------------------------------------------ work
        void StartWork()
        {
            Busy = true;
            var disp = Root.Dispatcher;
            string dir = installDir;
            var opts = new Installer.Options { Dir = dir, Desktop = optDesktop, StartMenu = optStart, AutoStart = optAuto, Wipe = optWipe };
            Action<double, string> rep = (f, msg) => disp.BeginInvoke(new Action(() => Progress(f, msg)));
            var th = new Thread(() =>
            {
                string err = null;
                try { if (un) Installer.Uninstall(opts, rep); else Installer.Install(opts, rep); }
                catch (Exception ex) { err = ex.Message; }
                Thread.Sleep(350);
                disp.BeginInvoke(new Action(() =>
                {
                    Busy = false;
                    failed = err != null; error = err;
                    finishedOk = !failed;
                    ShowPage(DonePage, true);
                }));
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
        }

        void Progress(double f, string msg)
        {
            if (barFill == null) return;
            G.Anim(barFill, FrameworkElement.WidthProperty, Math.Max(10, BarW * f), 420, G.Out);
            statusText.Text = msg;
            pctText.Text = Math.Round(f * 100) + "%";
        }

        public void FailForSnapshot(string msg)
        {
            failed = true; error = msg;
            ShowPage(DonePage, false);
        }

        void Finish()
        {
            if (RequestClose != null) RequestClose();
        }

        public void OnClosed()
        {
            if (!finishedOk) return;
            if (un) Installer.ScheduleSelfDelete(installDir);
            else if (optLaunch) Installer.Launch(installDir);
        }
    }

    // =====================================================================================
    static class Installer
    {
        public class Options { public string Dir; public bool Desktop, StartMenu, AutoStart, Wipe; }

        const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PrismDock";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PrismDock"); }
        }

        static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PrismDock.lnk"); } }
        static string StartLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PrismDock.lnk"); } }

        static long payload = -1;
        public static long PayloadSize
        {
            get
            {
                if (payload < 0)
                {
                    payload = 0;
                    try { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PrismDock.exe")) if (s != null) payload = s.Length; } catch { }
                    try { payload += new FileInfo(Assembly.GetExecutingAssembly().Location).Length; } catch { }
                }
                return payload;
            }
        }

        public static string FindInstallDir()
        {
            try
            {
                string self = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (File.Exists(Path.Combine(self, "PrismDock.exe")) && Path.GetFileName(Assembly.GetExecutingAssembly().Location).Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase)) return self;
            }
            catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstKey))
                {
                    var v = k != null ? k.GetValue("InstallLocation") as string : null;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }
            return DefaultDir;
        }

        static void Step(Action<double, string> rep, double f, string msg) { rep(f, msg); Thread.Sleep(220); }

        /// Asks a running PrismDock to quit (it restores the taskbar), kills it as a last resort.
        static void QuitRunning(string fallbackExe)
        {
            var procs = Process.GetProcessesByName("PrismDock");
            if (procs.Length == 0) return;
            string exe = null;
            try { exe = procs[0].MainModule.FileName; } catch { }
            if (exe == null || !File.Exists(exe)) exe = fallbackExe;
            bool haveExe = exe != null && File.Exists(exe);
            if (haveExe) RunWait(exe, "--quit", 3000);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 6000 && Process.GetProcessesByName("PrismDock").Length > 0) Thread.Sleep(200);
            var left = Process.GetProcessesByName("PrismDock");
            if (left.Length == 0) return;
            foreach (var p in left) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
            if (haveExe) RunWait(exe, "--restore", 5000);
        }

        static void RunWait(string exe, string args, int ms)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
                if (p != null) p.WaitForExit(ms);
            }
            catch { }
        }

        public static void Install(Options o, Action<double, string> rep)
        {
            string dir = o.Dir, exe = Path.Combine(dir, "PrismDock.exe");
            Step(rep, 0.06, "Çalışan PrismDock kapatılıyor…");
            QuitRunning(exe);

            Step(rep, 0.22, "Klasör hazırlanıyor…");
            Directory.CreateDirectory(dir);

            Step(rep, 0.38, "Dosyalar kopyalanıyor…");
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PrismDock.exe"))
            {
                if (s == null) throw new InvalidOperationException("Kurulum paketi bozuk: PrismDock.exe bulunamadı.");
                string tmp = exe + ".new";
                using (var f = File.Create(tmp)) s.CopyTo(f);
                if (File.Exists(exe)) File.Delete(exe);
                File.Move(tmp, exe);
            }
            string self = Assembly.GetExecutingAssembly().Location, un = Path.Combine(dir, "Uninstall.exe");
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(un), StringComparison.OrdinalIgnoreCase)) File.Copy(self, un, true);

            Step(rep, 0.58, "Kısayollar oluşturuluyor…");
            if (o.Desktop) MakeLnk(DesktopLnk, exe); else DeleteFile(DesktopLnk);
            if (o.StartMenu) MakeLnk(StartLnk, exe); else DeleteFile(StartLnk);

            Step(rep, 0.76, "Kayıt bilgileri yazılıyor…");
            using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
            {
                k.SetValue("DisplayName", "PrismDock");
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "PrismDock");
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + un + "\" --uninstall");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)Math.Max(1, (new FileInfo(exe).Length + new FileInfo(un).Length) / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            Step(rep, 0.9, "Başlangıç ayarlanıyor…");
            // start at sign-in through a logon task (immediate); the Run key would wait behind every other startup app
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                bool task = o.AutoStart && LogonTask(exe);
                if (o.AutoStart && !task) k.SetValue("PrismDock", "\"" + exe + "\"");   // fallback
                else if (k.GetValue("PrismDock") != null) k.DeleteValue("PrismDock");
                if (!o.AutoStart) Schtasks("/delete /tn \"PrismDock Autostart\" /f");
                Schtasks("/delete /tn \"PrismDock\" /f");   // name used by 2.1.x
            }
            Step(rep, 1.0, "Tamamlandı");
        }

        static int Schtasks(string args)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false }))
                { p.WaitForExit(10000); return p.ExitCode; }
            }
            catch { return -1; }
        }

        static bool LogonTask(string exe)
        {
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>PrismDock</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + System.Security.SecurityElement.Escape(user) + "</UserId></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"Author\"><UserId>" + System.Security.SecurityElement.Escape(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>\"" + System.Security.SecurityElement.Escape(exe) + "\"</Command><WorkingDirectory>" + System.Security.SecurityElement.Escape(Path.GetDirectoryName(exe)) + "</WorkingDirectory></Exec></Actions></Task>";
            string f = Path.Combine(Path.GetTempPath(), "prismdock-task.xml");
            try { File.WriteAllText(f, xml, System.Text.Encoding.Unicode); return Schtasks("/create /tn \"PrismDock Autostart\" /xml \"" + f + "\" /f") == 0; }
            catch { return false; }
            finally { try { File.Delete(f); } catch { } }
        }

        public static void Uninstall(Options o, Action<double, string> rep)
        {
            string dir = o.Dir, exe = Path.Combine(dir, "PrismDock.exe");
            Step(rep, 0.1, "PrismDock kapatılıyor…");
            QuitRunning(exe);

            Step(rep, 0.32, "Kısayollar kaldırılıyor…");
            DeleteFile(DesktopLnk); DeleteFile(StartLnk);

            Step(rep, 0.52, "Kayıt bilgileri temizleniyor…");
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (k != null && k.GetValue("PrismDock") != null) k.DeleteValue("PrismDock");
            Schtasks("/delete /tn \"PrismDock Autostart\" /f");
            Schtasks("/delete /tn \"PrismDock\" /f");
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); } catch { }

            if (o.Wipe)
            {
                Step(rep, 0.7, "Ayarlar ve notlar siliniyor…");
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismDock");
                try { if (Directory.Exists(data)) Directory.Delete(data, true); } catch { }
            }

            Step(rep, 0.86, "Dosyalar siliniyor…");
            for (int i = 0; i < 10 && File.Exists(exe); i++) { try { File.Delete(exe); } catch { Thread.Sleep(300); } }
            if (File.Exists(exe)) throw new IOException("PrismDock.exe silinemedi (hâlâ kullanımda olabilir).");
            Step(rep, 1.0, "Tamamlandı");
        }

        /// Removes Uninstall.exe and the (then empty) install folder. A running exe cannot be deleted but it can be
        /// moved: Uninstall.exe goes to %TEMP% (cleaned up on the next setup run), then the empty folder is removed.
        /// Only our own files are touched; the folder is removed only when nothing else is left in it.
        public static void ScheduleSelfDelete(string dir)
        {
            try
            {
                string self = Assembly.GetExecutingAssembly().Location;
                dir = dir.TrimEnd('\\');
                if (string.Equals(Path.GetDirectoryName(self), dir, StringComparison.OrdinalIgnoreCase))
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "PrismDock-Uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                    try { File.Move(self, tmp); } catch { }
                }
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch { }
        }

        /// leftovers of earlier uninstalls (see ScheduleSelfDelete)
        public static void CleanTemp()
        {
            try { foreach (var f in Directory.GetFiles(Path.GetTempPath(), "PrismDock-Uninstall-*.exe")) DeleteFile(f); }
            catch { }
        }

        public static void Launch(string dir)
        {
            try
            {
                string exe = Path.Combine(dir, "PrismDock.exe");
                if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir });
            }
            catch { }
        }

        static void MakeLnk(string lnk, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic sc = sh.CreateShortcut(lnk);
            sc.TargetPath = target;
            sc.WorkingDirectory = Path.GetDirectoryName(target);
            sc.IconLocation = target + ",0";
            sc.Description = "PrismDock - liquid glass dock";
            sc.Save();
        }

        static void DeleteFile(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    }
}
