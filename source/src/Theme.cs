using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PrismDock
{
    class Theme
    {
        public string Id, Name;
        public int AccentState;      // 0 none, 3 blur, 4 acrylic
        public uint AccentColor;     // AABBGGRR
        public Color[] Tint;         // horizontal tint stops
        public Color FillTop, FillBottom, Rim1, Rim2, Sheen, Fg, Sub, Hover, Accent, PanelExtra;
        public bool Dark = true;
        // live glass optics
        public double CaptureScale = 1.0;   // < 1: downsampled capture = frosted blur
        public double BlurMul = 1.0, RefractMul = 1.0, Chroma = 0.22, Saturation = 1.25;

        public static Theme Cur;
        public static readonly List<Theme> All = new List<Theme>();

        static Color C(double a, byte r, byte g, byte b) { return Color.FromArgb((byte)(a * 255), r, g, b); }
        static uint Abgr(byte a, byte r, byte g, byte b) { return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r; }

        static Theme()
        {
            All.Add(new Theme
            {
                Id = "liquid", Name = "Liquid Glass", AccentState = 4, AccentColor = Abgr(0x30, 14, 16, 22),
                CaptureScale = 1.0, BlurMul = 0.8, RefractMul = 1.0, Chroma = 0.25, Saturation = 1.35,
                Tint = new[] { C(0.0, 255, 255, 255) },
                FillTop = C(0.14, 255, 255, 255), FillBottom = C(0.03, 255, 255, 255),
                Rim1 = C(0.75, 255, 255, 255), Rim2 = C(0.18, 255, 255, 255), Sheen = C(0.16, 255, 255, 255),
                Fg = Colors.White, Sub = C(0.72, 255, 255, 255), Hover = C(0.18, 255, 255, 255), Accent = Colors.White,
                PanelExtra = C(0.30, 20, 22, 30)
            });
            All.Add(new Theme
            {
                Id = "glass", Name = "Buzlu Cam", AccentState = 4, AccentColor = Abgr(0x40, 20, 22, 30),
                CaptureScale = 0.34, BlurMul = 1.0, RefractMul = 0.7, Chroma = 0.15, Saturation = 1.4,
                Tint = new[] { C(0.0, 255, 255, 255) },
                FillTop = C(0.16, 255, 255, 255), FillBottom = C(0.05, 255, 255, 255),
                Rim1 = C(0.65, 255, 255, 255), Rim2 = C(0.14, 255, 255, 255), Sheen = C(0.14, 255, 255, 255),
                Fg = Colors.White, Sub = C(0.72, 255, 255, 255), Hover = C(0.18, 255, 255, 255), Accent = Colors.White,
                PanelExtra = C(0.25, 20, 22, 30)
            });
            All.Add(new Theme
            {
                Id = "clear", Name = "Kristal", AccentState = 3, AccentColor = Abgr(0x10, 0, 0, 0),
                CaptureScale = 1.0, BlurMul = 0.0, RefractMul = 1.35, Chroma = 0.4, Saturation = 1.15,
                Tint = new[] { C(0.0, 0, 0, 0) },
                FillTop = C(0.12, 255, 255, 255), FillBottom = C(0.02, 255, 255, 255),
                Rim1 = C(0.55, 255, 255, 255), Rim2 = C(0.12, 255, 255, 255), Sheen = C(0.16, 255, 255, 255),
                Fg = Colors.White, Sub = C(0.72, 255, 255, 255), Hover = C(0.16, 255, 255, 255), Accent = Colors.White,
                PanelExtra = C(0.35, 0, 0, 0)
            });
            All.Add(new Theme
            {
                Id = "frost", Name = "Buz Beyazı", AccentState = 4, AccentColor = Abgr(0x98, 242, 242, 247), Dark = false,
                CaptureScale = 0.34, RefractMul = 0.7, Chroma = 0.1, Saturation = 1.3,
                Tint = new[] { C(0.0, 255, 255, 255) },
                FillTop = C(0.55, 255, 255, 255), FillBottom = C(0.30, 255, 255, 255),
                Rim1 = C(0.95, 255, 255, 255), Rim2 = C(0.45, 255, 255, 255), Sheen = C(0.45, 255, 255, 255),
                Fg = Color.FromRgb(28, 28, 30), Sub = Color.FromRgb(110, 110, 115), Hover = C(0.08, 0, 0, 0),
                Accent = Color.FromRgb(0, 122, 255), PanelExtra = C(0.0, 255, 255, 255)
            });
            All.Add(new Theme
            {
                Id = "midnight", CaptureScale = 0.34, RefractMul = 0.7, Chroma = 0.12, Saturation = 1.35, Name = "Gece Yarısı", AccentState = 4, AccentColor = Abgr(0xB4, 10, 14, 32),
                Tint = new[] { C(0.30, 70, 90, 190), C(0.22, 40, 40, 120) },
                FillTop = C(0.12, 160, 180, 255), FillBottom = C(0.02, 160, 180, 255),
                Rim1 = C(0.50, 150, 170, 255), Rim2 = C(0.10, 150, 170, 255), Sheen = C(0.14, 190, 205, 255),
                Fg = Color.FromRgb(234, 238, 255), Sub = C(0.65, 210, 220, 255), Hover = C(0.16, 160, 180, 255),
                Accent = Color.FromRgb(124, 156, 255), PanelExtra = C(0.0, 0, 0, 0)
            });
            All.Add(new Theme
            {
                Id = "aurora", CaptureScale = 0.34, RefractMul = 0.7, Chroma = 0.12, Saturation = 1.35, Name = "Aurora", AccentState = 4, AccentColor = Abgr(0x96, 8, 12, 22),
                Tint = new[] { C(0.30, 0, 200, 170), C(0.28, 120, 80, 255), C(0.26, 255, 90, 170) },
                FillTop = C(0.14, 255, 255, 255), FillBottom = C(0.02, 255, 255, 255),
                Rim1 = C(0.60, 220, 255, 250), Rim2 = C(0.12, 255, 200, 240), Sheen = C(0.16, 255, 255, 255),
                Fg = Colors.White, Sub = C(0.72, 255, 255, 255), Hover = C(0.18, 255, 255, 255),
                Accent = Color.FromRgb(94, 242, 208), PanelExtra = C(0.0, 0, 0, 0)
            });
            All.Add(new Theme
            {
                Id = "sunset", CaptureScale = 0.34, RefractMul = 0.7, Chroma = 0.12, Saturation = 1.35, Name = "Gün Batımı", AccentState = 4, AccentColor = Abgr(0x90, 26, 10, 20),
                Tint = new[] { C(0.32, 255, 140, 60), C(0.28, 255, 70, 130), C(0.28, 120, 60, 200) },
                FillTop = C(0.14, 255, 240, 230), FillBottom = C(0.02, 255, 240, 230),
                Rim1 = C(0.60, 255, 230, 210), Rim2 = C(0.12, 255, 180, 200), Sheen = C(0.16, 255, 245, 235),
                Fg = Colors.White, Sub = C(0.75, 255, 240, 235), Hover = C(0.18, 255, 255, 255),
                Accent = Color.FromRgb(255, 190, 120), PanelExtra = C(0.0, 0, 0, 0)
            });
            All.Add(new Theme
            {
                Id = "obsidian", Name = "Obsidyen", AccentState = 0, AccentColor = 0,
                Tint = new[] { Color.FromRgb(20, 20, 23), Color.FromRgb(14, 14, 16) },
                FillTop = C(0.06, 255, 255, 255), FillBottom = C(0.0, 255, 255, 255),
                Rim1 = C(0.16, 255, 255, 255), Rim2 = C(0.05, 255, 255, 255), Sheen = C(0.05, 255, 255, 255),
                Fg = Colors.White, Sub = C(0.6, 255, 255, 255), Hover = C(0.10, 255, 255, 255),
                Accent = Color.FromRgb(10, 132, 255), PanelExtra = C(0.0, 0, 0, 0)
            });
            Cur = All[0];
        }

        public static Theme Get(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return All[0];
        }

        /// tint laid over the blurred wallpaper (former DWM accent color, AABBGGRR)
        public Color Overlay
        {
            get
            {
                uint c = AccentColor;
                return Color.FromArgb((byte)(c >> 24), (byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
            }
        }
        public bool Opaque { get { return AccentState == 0; } }

        public Brush TintBrush()
        {
            if (Tint.Length == 1) return Ui.Br(Tint[0]);
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            for (int i = 0; i < Tint.Length; i++) g.GradientStops.Add(new GradientStop(Tint[i], (double)i / (Tint.Length - 1)));
            g.Freeze();
            return g;
        }
    }

    /// Applies DWM blur / acrylic and rounded window region.
    static class Glass
    {
        public static void Apply(IntPtr hwnd, Theme t)
        {
            string mode = Environment.GetEnvironmentVariable("PRISM_GLASS") ?? "";
            if (!mode.Contains("noext")) { var m = new Native.MARGINS { L = -1, R = -1, T = -1, B = -1 }; Native.DwmExtendFrameIntoClientArea(hwnd, ref m); }
            int st = t.AccentState; if (mode.Contains("blur") && st == 4) st = 3;
            var acc = new Native.AccentPolicy { AccentState = st, AccentFlags = 2, GradientColor = t.AccentColor };
            int size = System.Runtime.InteropServices.Marshal.SizeOf(acc);
            IntPtr p = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(acc, p, false);
                var d = new Native.WinCompAttrData { Attribute = 19, Data = p, SizeOfData = size };
                Native.SetWindowCompositionAttribute(hwnd, ref d);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(p); }
            // no DWM shadow/border on Win11 (we draw our own rim)
            int pref = 1; // DWMWCP_DONOTROUND
            Native.DwmSetWindowAttribute(hwnd, 33, ref pref, 4);
        }

        /// Non-layered window with per-pixel alpha through DWM (cheap, GPU composed, anti-aliased edges).
        public static void SetupDwm(Window w, IntPtr hwnd)
        {
            PrepareSource(w);
            var m = new Native.MARGINS { L = -1, R = -1, T = -1, B = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
            int pref = 1; Native.DwmSetWindowAttribute(hwnd, 33, ref pref, 4); // no Win11 rounding/border
            int ncr = 1; Native.DwmSetWindowAttribute(hwnd, 2, ref ncr, 4);    // DWMNCRP_DISABLED: no frame shadow
        }

        /// Bounds of el (with its render transforms) in window DIP coordinates.
        public static Rect Bounds(FrameworkElement el, Window w)
        {
            try { return el.TransformToAncestor(w).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight)); }
            catch { return Rect.Empty; }
        }

        /// Clips the window (shape + hit testing) to a rounded rect slightly larger than the drawn glass,
        /// so the region's hard pixel edge lies in fully transparent pixels and the WPF edge stays anti-aliased.
        public static void ClipTo(IntPtr hwnd, Rect r, double radius, double scale, ref Int32Rect last)
        {
            if (hwnd == IntPtr.Zero) return;
            Int32Rect k;
            if (r.IsEmpty || r.Width < 1 || r.Height < 1) k = new Int32Rect(0, 0, 0, 0);
            else
            {
                r.Inflate(1.5, 1.5);
                k = new Int32Rect((int)Math.Floor(r.Left * scale), (int)Math.Floor(r.Top * scale), (int)Math.Ceiling(r.Width * scale), (int)Math.Ceiling(r.Height * scale));
            }
            int d = (int)Math.Round((radius + 1.5) * 2 * scale);
            var key = new Int32Rect(k.X, k.Y, k.Width, k.Height * 4096 + Math.Min(d, 4095));
            if (key == last) return;
            last = key;
            IntPtr rgn = k.Width == 0 ? Native.CreateRectRgn(0, 0, 0, 0)
                : (d > 2 ? Native.CreateRoundRectRgn(k.X, k.Y, k.X + k.Width + 1, k.Y + k.Height + 1, d, d) : Native.CreateRectRgn(k.X, k.Y, k.X + k.Width, k.Y + k.Height));
            Native.SetWindowRgn(hwnd, rgn, true);
        }

        public static void PrepareSource(Window w)
        {
            var src = HwndSource.FromHwnd(new WindowInteropHelper(w).Handle);
            if (src != null && src.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        public static double Scale(Visual v)
        {
            var src = PresentationSource.FromVisual(v);
            if (src != null && src.CompositionTarget != null) return src.CompositionTarget.TransformToDevice.M11;
            return 1.0;
        }

        /// Sets window region (in DIP coordinates of the window) to a rounded rect.
        public static void SetRegion(IntPtr hwnd, Rect r, double radius, double scale)
        {
            int x1 = (int)Math.Round(r.Left * scale), y1 = (int)Math.Round(r.Top * scale);
            int x2 = (int)Math.Round(r.Right * scale), y2 = (int)Math.Round(r.Bottom * scale);
            int d = (int)Math.Round(radius * 2 * scale);
            IntPtr rgn = d > 1 ? Native.CreateRoundRectRgn(x1, y1, x2 + 1, y2 + 1, d, d) : Native.CreateRectRgn(x1, y1, x2, y2);
            int ok = Native.SetWindowRgn(hwnd, rgn, true); // system owns rgn afterwards
            if (Environment.GetEnvironmentVariable("PRISM_DEBUG") != null)
                System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Dir, "debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " rgn " + hwnd + " " + x1 + "," + y1 + "," + x2 + "," + y2 + " d=" + d + " ok=" + ok + Environment.NewLine);
        }
    }
}
