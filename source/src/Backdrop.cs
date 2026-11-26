using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace PrismDock
{
    /// GPU refraction shader: bends the backdrop near the rounded edges (liquid glass lens),
    /// adds chromatic dispersion, a soft blur and a saturation boost. Compiled once with
    /// d3dcompiler_47 (ships with Windows) and cached on disk.
    class GlassEffect : ShaderEffect
    {
        const string Hlsl = @"
sampler2D input : register(s0);
float4 p0 : register(c0);
float4 p1 : register(c1);
float sdBox(float2 p, float2 b, float r) { float2 q = abs(p) - b + r; return length(max(q, 0)) + min(max(q.x, q.y), 0) - r; }
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 sz = max(p0.xy, 2);
    float2 hb = sz * 0.5;
    float2 p = uv * sz - hb;
    float r = min(p0.z, min(hb.x, hb.y));
    float d = sdBox(p, hb, r);
    float2 g = float2(sdBox(p + float2(1, 0), hb, r) - sdBox(p - float2(1, 0), hb, r),
                      sdBox(p + float2(0, 1), hb, r) - sdBox(p - float2(0, 1), hb, r));
    float2 n = g / (length(g) + 0.0001);
    float t = saturate(1 + d / max(p1.z, 1));
    float k = t * t;
    float2 off = -n * (k * p0.w) / sz;
    float2 b = uv + off;
    float2 ch = off * p1.y;
    float3 c;
    c.r = tex2D(input, b + ch).r;
    c.g = tex2D(input, b).g;
    c.b = tex2D(input, b - ch).b;
    float2 px = p1.x / sz;
    float3 s = tex2D(input, b + px * float2(1, 0)).rgb + tex2D(input, b - px * float2(1, 0)).rgb
             + tex2D(input, b + px * float2(0, 1)).rgb + tex2D(input, b - px * float2(0, 1)).rgb
             + tex2D(input, b + px * float2(0.7, 0.7)).rgb + tex2D(input, b - px * float2(0.7, 0.7)).rgb
             + tex2D(input, b + px * float2(0.7, -0.7)).rgb + tex2D(input, b - px * float2(0.7, -0.7)).rgb;
    c = (c * 2 + s) / 10;
    float l = dot(c, float3(0.299, 0.587, 0.114));
    c = saturate(lerp(float3(l, l, l), c, p1.w));
    float a = tex2D(input, uv).a;
    return float4(c * a, a);
}";
        const string Precompiled = "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";
        static PixelShader shader;
        static bool failed;

        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(GlassEffect), 0);
        public static readonly DependencyProperty P0Property = DependencyProperty.Register("P0", typeof(Point4D), typeof(GlassEffect),
            new UIPropertyMetadata(new Point4D(100, 50, 25, 0), PixelShaderConstantCallback(0)));
        public static readonly DependencyProperty P1Property = DependencyProperty.Register("P1", typeof(Point4D), typeof(GlassEffect),
            new UIPropertyMetadata(new Point4D(0, 0, 10, 1), PixelShaderConstantCallback(1)));

        public static GlassEffect Create()
        {
            var ps = Shader();
            if (ps == null) return null;
            var e = new GlassEffect { PixelShader = ps };
            e.UpdateShaderValue(InputProperty);
            e.UpdateShaderValue(P0Property);
            e.UpdateShaderValue(P1Property);
            return e;
        }

        /// size in device px, corner radius px, refraction px | blur px, chroma, edge px, saturation
        public void Set(double w, double h, double radius, double refract, double blur, double chroma, double edge, double sat)
        {
            var a = new Point4D(w, h, radius, refract);
            var b = new Point4D(blur, chroma, edge, sat);
            if ((Point4D)GetValue(P0Property) != a) SetValue(P0Property, a);
            if ((Point4D)GetValue(P1Property) != b) SetValue(P1Property, b);
        }

        static PixelShader Shader()
        {
            if (shader != null || failed) return shader;
            try
            {
                // precompiled ps_3_0 bytecode ships inside the exe: no shader compiler work at startup
                byte[] code = null;
                try { code = Convert.FromBase64String(Precompiled); } catch { }
                string cache = Path.Combine(Settings.Dir, "glass_v3.ps");
                if (code == null && File.Exists(cache)) code = File.ReadAllBytes(cache);
                if (code == null)
                {
                    code = Compile(Hlsl);
                    try { File.WriteAllBytes(cache, code); } catch { }
                }
                var s = new PixelShader();
                s.SetStreamSource(new MemoryStream(code));
                s.Freeze();
                shader = s;
            }
            catch { failed = true; }
            return shader;
        }

        static byte[] Compile(string src)
        {
            var b = System.Text.Encoding.ASCII.GetBytes(src);
            Native.ID3DBlob code, err;
            int hr = Native.D3DCompile(b, (IntPtr)b.Length, "glass", IntPtr.Zero, IntPtr.Zero, "main", "ps_3_0", 1 << 15, 0, out code, out err);
            if (hr < 0 || code == null) throw new Exception(err != null ? Marshal.PtrToStringAnsi(err.GetBufferPointer()) : "D3DCompile " + hr);
            var r = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), r, 0, r.Length);
            return r;
        }
    }

    /// Captures the screen behind one of our windows. The window is excluded from the capture only
    /// for the duration of the BitBlt (display affinity), so it still shows up in the user's screenshots.
    /// Adaptive rate: ~25 fps while the background changes, ~4 fps once it is still.
    class LiveCapture : IDisposable
    {
        readonly IntPtr owner;
        readonly Dispatcher disp;
        readonly Action applyAction;
        readonly object capLock = new object();   // DIB / buffers are touched by the pacer thread and the UI thread
        IntPtr pendingBuf; volatile bool uiPending; static bool affinityWarned;
        internal long due;
        internal volatile int period = 20;
        bool running;
        IntPtr sdc, mdc, dib, oldBmp, bits;
        int bw, bh;
        Int32Rect src;
        double factor = 1;
        uint lastSum;
        int still;
        public WriteableBitmap Bitmap;
        public Rect ScreenPx;
        public event Action Reallocated;

        public LiveCapture(IntPtr owner)
        {
            this.owner = owner;
            disp = Dispatcher.CurrentDispatcher;
            applyAction = Apply;
        }

        public bool Running { get { return running; } }
        public bool Suspended;

        /// rect in physical screen pixels; scale < 1 captures a downsampled (= blurred) image.
        public void SetRect(Int32Rect r, double scale)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            int w = Math.Max(1, (int)Math.Ceiling(r.Width * scale)), h = Math.Max(1, (int)Math.Ceiling(r.Height * scale));
            src = r; factor = scale;
            ScreenPx = new Rect(r.X, r.Y, r.Width, r.Height);
            if (w != bw || h != bh || Bitmap == null)
            {
                lock (capLock) { Free(); bw = w; bh = h; Alloc(); }
                Bitmap = new WriteableBitmap(bw, bh, 96, 96, PixelFormats.Bgr32, null);
                lastSum = 0;
                Grab(true);
                if (Reallocated != null) Reallocated();
            }
            else Grab(true);
        }

        void Alloc()
        {
            {
                sdc = Native.GetDC(IntPtr.Zero);
                mdc = Native.CreateCompatibleDC(sdc);
                var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = bw, biHeight = -bh, biPlanes = 1, biBitCount = 32 };
                dib = Native.CreateDIBSection(sdc, ref bi, 0, out bits, IntPtr.Zero, 0);
                oldBmp = Native.SelectObject(mdc, dib);
                Native.SetStretchBltMode(mdc, 4); // HALFTONE: proper box filtering when downsampling
                pendingBuf = System.Runtime.InteropServices.Marshal.AllocHGlobal(bw * bh * 4);
            }
        }

        int ActiveMs = 20, StillMs = 33;   // 50 fps while the background moves, 30 fps when still
        /// docks on extra monitors: 30 fps moving / 10 fps still keeps them live at a fraction of the cost
        public void SetRates(int activeMs, int stillMs) { ActiveMs = activeMs; StillMs = stillMs; period = activeMs; }

        public void Start()
        {
            if (running) return;
            running = true; still = 0; period = ActiveMs;
            Grab(true);
            Pacer.Add(this);
        }
        public void Stop() { if (!running) return; running = false; Pacer.Remove(this); }
        public void Kick() { still = 0; period = ActiveMs; }

        /// Runs on the pacer thread: screen copy, change check and staging happen here, off the UI thread.
        /// The UI thread only gets work when the picture really changed (one memcpy + render).
        internal void Capture()
        {
            if (!running) return;
            if (Suspended || !Native.IsWindowVisible(owner)) return;
            bool changed = false;
            lock (capLock)
            {
                if (mdc == IntPtr.Zero || pendingBuf == IntPtr.Zero) return;
                Blit();
                uint sum = Checksum();
                if (sum != lastSum)
                {
                    lastSum = sum;
                    Native.CopyMemory(pendingBuf, bits, (IntPtr)(bw * bh * 4));
                    changed = true;
                }
            }
            if (changed)
            {
                period = ActiveMs; still = 0;
                if (!uiPending) { uiPending = true; disp.BeginInvoke(DispatcherPriority.Render, applyAction); }
            }
            else if (++still >= 6) period = StillMs;
        }

        /// Windows 11: switching the capture exclusion on and off makes DWM rebuild the window's redirection every
        /// frame, which drags other apps (Settings, Clock, Calendar ...) down to a few fps. There it stays on for good
        /// (the dock then doesn't appear in screenshots); Windows 10 keeps the cheap per-frame toggle.
        static readonly bool stickyAffinity = EventLog.WindowsBuild >= 22000;
        bool affinityOn;

        // how long one screen copy takes (EMA, ms). A weak / software GPU (school PCs, VMs) makes every copy stall
        // the compositor: slower than 12 ms -> 15/5 fps, slower than 25 ms -> live glass off (blurred wallpaper).
        double blitMs = -1; int blitN; int slowStage;
        public static volatile bool TooSlow;
        public static event Action SlowChanged;

        void Blit()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!stickyAffinity || !affinityOn)
            {
                bool ok = Native.SetWindowDisplayAffinity(owner, 0x11); // WDA_EXCLUDEFROMCAPTURE
                if (!ok && !affinityWarned) { affinityWarned = true; EventLog.Write("capture: SetWindowDisplayAffinity failed"); }
                affinityOn = ok;
            }
            if (factor >= 0.999) Native.BitBlt(mdc, 0, 0, bw, bh, sdc, src.X, src.Y, 0x00CC0020);
            else Native.StretchBlt(mdc, 0, 0, bw, bh, sdc, src.X, src.Y, src.Width, src.Height, 0x00CC0020);
            if (!stickyAffinity) { Native.SetWindowDisplayAffinity(owner, 0); affinityOn = false; }
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            blitMs = blitMs < 0 ? ms : blitMs * 0.95 + ms * 0.05;
            if (++blitN < 60) return;   // judge after a couple of seconds
            if (slowStage == 0 && blitMs > 12)
            {
                slowStage = 1; ActiveMs = 66; StillMs = 200; period = ActiveMs;
                EventLog.Write("capture: screen copy slow (" + blitMs.ToString("0.0") + " ms) -> 15 fps");
            }
            else if (slowStage == 1 && blitN > 200 && blitMs > 25 && !TooSlow)
            {
                slowStage = 2; TooSlow = true;
                EventLog.Write("capture: screen copy too slow (" + blitMs.ToString("0.0") + " ms) -> live glass off");
                disp.BeginInvoke(new Action(() => { if (SlowChanged != null) SlowChanged(); }));
            }
        }

        /// UI thread: newest staged frame -> WriteableBitmap
        void Apply()
        {
            lock (capLock)
            {
                uiPending = false;
                if (Bitmap == null || pendingBuf == IntPtr.Zero || Bitmap.PixelWidth != bw || Bitmap.PixelHeight != bh) return;
                Bitmap.Lock();
                Native.CopyMemory(Bitmap.BackBuffer, pendingBuf, (IntPtr)(bw * bh * 4));
                Bitmap.AddDirtyRect(new Int32Rect(0, 0, bw, bh));
                Bitmap.Unlock();
            }
        }

        /// synchronous capture on the UI thread (first frame after start / resize)
        bool Grab(bool force)
        {
            lock (capLock)
            {
                if (mdc == IntPtr.Zero || Bitmap == null) return false;
                Blit();
                uint sum = Checksum();
                if (!force && sum == lastSum) return false;
                lastSum = sum;
                Bitmap.Lock();
                Native.CopyMemory(Bitmap.BackBuffer, bits, (IntPtr)(bw * bh * 4));
                Bitmap.AddDirtyRect(new Int32Rect(0, 0, bw, bh));
                Bitmap.Unlock();
                return true;
            }
        }

        unsafe uint Checksum()
        {
            uint* p = (uint*)bits;
            int n = bw * bh;
            uint h = 2166136261;
            for (int i = 0; i < n; i += 7) h = (h ^ p[i]) * 16777619;
            return h;
        }

        void Free()
        {
            if (pendingBuf != IntPtr.Zero) { System.Runtime.InteropServices.Marshal.FreeHGlobal(pendingBuf); pendingBuf = IntPtr.Zero; }
            if (mdc != IntPtr.Zero) { Native.SelectObject(mdc, oldBmp); Native.DeleteDC(mdc); mdc = IntPtr.Zero; }
            if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
            if (sdc != IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, sdc); sdc = IntPtr.Zero; }
            bw = bh = 0;
        }

        public void Dispose() { Stop(); lock (capLock) Free(); Bitmap = null; }
    }

    /// Precise frame pacing for the captures (WPF/USER timers are quantised to 15.6 ms, which caps at ~32 fps).
    /// One background thread sleeps with 1 ms timer resolution and posts each capture to its UI thread on time.
    static class Pacer
    {
        static readonly List<LiveCapture> list = new List<LiveCapture>();
        static readonly object gate = new object();
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        static System.Threading.Thread th;
        static readonly System.Threading.AutoResetEvent wake = new System.Threading.AutoResetEvent(false);

        public static void Add(LiveCapture c)
        {
            lock (gate)
            {
                if (list.Contains(c)) return;
                c.due = clock.ElapsedMilliseconds + c.period;
                list.Add(c);
                if (list.Count == 1) Native.timeBeginPeriod(1);
                if (th == null)
                {
                    th = new System.Threading.Thread(Loop) { IsBackground = true, Name = "PrismDock pacer", Priority = System.Threading.ThreadPriority.AboveNormal };
                    th.Start();
                }
            }
            wake.Set();
        }

        public static void Remove(LiveCapture c)
        {
            lock (gate)
            {
                if (list.Remove(c) && list.Count == 0) Native.timeEndPeriod(1);
            }
        }

        static void Loop()
        {
            var due = new List<LiveCapture>();
            while (true)
            {
                long wait;
                due.Clear();
                lock (gate)
                {
                    long now = clock.ElapsedMilliseconds;
                    wait = 1000;
                    foreach (var c in list)
                    {
                        if (now >= c.due)
                        {
                            c.due += c.period;
                            if (c.due <= now) c.due = now + c.period;   // fell behind: don't burst
                            due.Add(c);
                        }
                        wait = Math.Min(wait, c.due - now);
                    }
                    if (list.Count == 0) wait = -1;
                }
                foreach (var c in due) { try { c.Capture(); } catch { } }
                if (wait < 0) wake.WaitOne();
                else if (wait > 0) wake.WaitOne((int)wait);
            }
        }
    }

    /// One per window: owns the image behind the window (live capture or blurred wallpaper)
    /// and keeps every glass layer of that window aligned with it.
    class BackdropHost
    {
        readonly Window win;
        readonly List<GlassLayer> layers = new List<GlassLayer>();
        LiveCapture live;
        bool frosted;
        public ImageSource Image;
        public Rect ScreenPx;
        public bool Slow;
        public bool IsLive { get { return live != null; } }

        public BackdropHost(Window w, bool frosted)
        {
            win = w; this.frosted = frosted;
        }

        public void Register(GlassLayer l) { if (!layers.Contains(l)) layers.Add(l); l.Host = this; l.Rebind(); }
        public void Unregister(GlassLayer l) { layers.Remove(l); }
        public void Clear() { layers.Clear(); }

        double CaptureScale { get { var t = Theme.Cur; return frosted ? Math.Min(t.CaptureScale, 0.34) : t.CaptureScale; } }

        /// (Re)configures source after theme / settings / window geometry change.
        public void Configure()
        {
            var t = Theme.Cur;
            IntPtr h = new WindowInteropHelper(win).Handle;
            bool wantLive = App.S.LiveGlass && !LiveCapture.TooSlow && !t.Opaque && h != IntPtr.Zero && GlassEffect.Create() != null;
            if (wantLive)
            {
                if (live == null)
                {
                    live = new LiveCapture(h);
                    if (Slow) live.SetRates(33, 100);
                    live.Reallocated += () => { Image = live.Bitmap; ScreenPx = live.ScreenPx; RebindAll(); };
                }
                double sc = Glass.Scale(win);
                var r = new Int32Rect((int)Math.Round(win.Left * sc), (int)Math.Round(win.Top * sc), (int)Math.Round(win.ActualWidth > 0 ? win.ActualWidth * sc : win.Width * sc), (int)Math.Round(win.ActualHeight > 0 ? win.ActualHeight * sc : win.Height * sc));
                live.Suspended = suspended;
                live.SetRect(r, CaptureScale);
                Image = live.Bitmap; ScreenPx = live.ScreenPx;
                if (win.IsVisible && !suspended) live.Start();
            }
            else
            {
                if (live != null) { live.Dispose(); live = null; }
                Image = t.Opaque ? null : Wallpaper.Blurred;
                ScreenPx = new Rect(0, 0, Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
            }
            RebindAll();
        }

        bool suspended;
        /// owner hidden (fullscreen app, closed panel): no capturing at all
        public bool Suspended
        {
            get { return suspended; }
            set { suspended = value; if (live != null) { live.Suspended = value; if (value) live.Stop(); else if (win.IsVisible) live.Start(); } }
        }

        public void SetFrosted(bool f) { if (f != frosted) { frosted = f; Configure(); } }
        public void Start() { suspended = false; if (live != null) { live.Suspended = false; live.Start(); } }
        public void Stop() { Suspended = true; }
        public void Kick() { if (live != null) live.Kick(); }

        void RebindAll() { foreach (var l in layers.ToArray()) l.Rebind(); }

        public void UpdateAll() { foreach (var l in layers) l.Align(); }

        bool alignPending;
        /// many things moving in one frame (drag + neighbours sliding): realign the glass once
        public void RequestAlign()
        {
            if (alignPending) return;
            alignPending = true;
            win.Dispatcher.BeginInvoke(new Action(() => { alignPending = false; UpdateAll(); }), DispatcherPriority.Render);
        }
    }

    /// A rounded backdrop layer: shows the window's backdrop image, aligned to screen, through the glass shader.
    class GlassLayer : System.Windows.Controls.Border
    {
        public BackdropHost Host;
        ImageBrush brush;
        GlassEffect fx;
        double radius, refract, edge, blurPx;
        public bool IgnoreRenderTransform;   // tiles: align to the layout slot so magnification acts as a lens
        public FrameworkElement AlignTo;      // optional untransformed element that defines position

        public GlassLayer(double radius, double refract, double edge, double blur)
        {
            this.radius = radius; this.refract = refract; this.edge = edge; blurPx = blur;
            CornerRadius = new CornerRadius(radius);
            SizeChanged += (s, e) => { UpdateFx(); Align(); };
        }

        public void SetShape(double radius, double refract, double edge, double blur)
        {
            this.radius = radius; this.refract = refract; this.edge = edge; blurPx = blur;
            CornerRadius = new CornerRadius(radius);
            UpdateFx();
        }

        public void Rebind()
        {
            if (Host == null || Host.Image == null) { Background = null; brush = null; Effect = null; fx = null; return; }
            brush = new ImageBrush(Host.Image) { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.Linear);
            Background = brush;
            if (fx == null) fx = GlassEffect.Create();
            Effect = fx;
            UpdateFx();
            Align();
        }

        void UpdateFx()
        {
            if (fx == null || ActualWidth <= 0) return;
            var t = Theme.Cur;
            double sc = Glass.Scale(this);
            double blur = Host != null && Host.IsLive ? blurPx * t.BlurMul : 0.5;
            double rf = refract * t.RefractMul * (App.S != null ? App.S.Refraction / 100.0 : 1);
            fx.Set(ActualWidth * sc, ActualHeight * sc, radius * sc, rf * sc, blur * sc, t.Chroma, edge * sc, t.Saturation);
        }

        public void Align()
        {
            if (brush == null || Host == null || ActualWidth <= 0) return;
            var src = PresentationSource.FromVisual(this);
            if (src == null || src.RootVisual == null) return;
            Rect b;
            try
            {
                var root = (Visual)src.RootVisual;
                if (AlignTo != null)
                {
                    var o = AlignTo.TransformToAncestor(root).Transform(new Point(0, 0));
                    var off = TranslatePoint(new Point(0, 0), AlignTo);
                    if (IgnoreRenderTransform) off = new Point((AlignTo.ActualWidth - ActualWidth) / 2, (AlignTo.ActualHeight - ActualHeight) / 2 + offsetY);
                    b = new Rect(o.X + off.X, o.Y + off.Y, ActualWidth, ActualHeight);
                }
                else b = TransformToAncestor(root).TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
                var org = ((UIElement)root).PointToScreen(new Point(0, 0));
                double sc = src.CompositionTarget.TransformToDevice.M11;
                var r = Host.ScreenPx;
                if (r.Width <= 0) return;
                double x = org.X + b.X * sc, y = org.Y + b.Y * sc;
                brush.Viewbox = new Rect((x - r.X) / r.Width, (y - r.Y) / r.Height, b.Width * sc / r.Width, b.Height * sc / r.Height);
            }
            catch { }
        }
        public double offsetY;
    }
}
