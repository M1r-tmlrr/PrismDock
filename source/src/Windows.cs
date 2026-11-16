using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrismDock
{
    class WinInfo
    {
        public IntPtr H; public string Exe; public string Title; public bool Minimized; public uint Pid;
    }

    static class Tracker
    {
        static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;
        static readonly HashSet<string> Ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "shellexperiencehost.exe", "searchapp.exe", "searchui.exe", "startmenuexperiencehost.exe",
            "textinputhost.exe", "lockapp.exe", "searchhost.exe", "widgets.exe"
        };
        static readonly Dictionary<uint, string> pidCache = new Dictionary<uint, string>();

        public static List<WinInfo> Enumerate()
        {
            var list = new List<WinInfo>();
            var seenPids = new HashSet<uint>();
            Native.EnumWindows((h, l) =>
            {
                if (!IsTaskWindow(h)) return true;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == OwnPid) return true;
                seenPids.Add(pid);
                uint realPid = pid;
                string exe = ExeFor(h, pid, ref realPid);
                if (exe != null && Ignore.Contains(Path.GetFileName(exe))) return true;
                list.Add(new WinInfo { H = h, Pid = realPid, Exe = exe != null ? exe.ToLowerInvariant() : null, Title = Native.GetText(h), Minimized = Native.IsIconic(h) });
                return true;
            }, IntPtr.Zero);
            // drop dead pids from cache
            if (pidCache.Count > 200) pidCache.Clear();
            return list;
        }

        public static bool IsTaskWindow(IntPtr h)
        {
            if (!Native.IsWindowVisible(h)) return false;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            bool appWin = (ex & Native.WS_EX_APPWINDOW) != 0;
            if (!appWin)
            {
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
                if ((ex & Native.WS_EX_NOACTIVATE) != 0) return false;
                if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
            }
            int cloaked;
            if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            if (Native.GetWindowTextLength(h) == 0) return false;
            string cls = Native.GetClass(h);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Windows.UI.Core.CoreWindow") return false;
            return true;
        }

        static string ExeFor(IntPtr h, uint pid, ref uint realPid)
        {
            string exe;
            if (!pidCache.TryGetValue(pid, out exe))
            {
                exe = Native.GetProcessPath(pid);
                pidCache[pid] = exe;
            }
            if (exe != null && exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                string child = null; uint childPid = 0;
                Native.EnumChildWindows(h, (c, l) =>
                {
                    uint cp; Native.GetWindowThreadProcessId(c, out cp);
                    if (cp != pid) { child = Native.GetProcessPath(cp); childPid = cp; return false; }
                    return true;
                }, IntPtr.Zero);
                if (child != null) { realPid = childPid; return child; }
                realPid = 0; // never kill the shared frame host
            }
            return exe;
        }

        public static void Activate(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            // ALT tap trick lets us steal foreground reliably
            Native.keybd_event(0x12, 0, 0, UIntPtr.Zero);
            Native.SetForegroundWindow(h);
            Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
        }

        public static void Minimize(IntPtr h) { Native.ShowWindow(h, Native.SW_MINIMIZE); }
        public static void Close(IntPtr h) { Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }

        /// End task: terminates the processes owning the app's windows (like Task Manager's "End task").
        public static int Kill(IEnumerable<WinInfo> wins)
        {
            int n = 0;
            var done = new HashSet<uint>();
            foreach (var w in wins)
            {
                if (w.Pid == 0 || w.Pid == OwnPid || !done.Add(w.Pid)) continue;
                try { using (var p = Process.GetProcessById((int)w.Pid)) { p.Kill(); n++; } }
                catch
                {
                    try { var psi = new ProcessStartInfo("taskkill.exe", "/PID " + w.Pid + " /T /F") { CreateNoWindow = true, UseShellExecute = false }; Process.Start(psi); n++; } catch { }
                }
            }
            return n;
        }

        public static bool IsFullscreen(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            string cls = Native.GetClass(h);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
            uint pid; Native.GetWindowThreadProcessId(h, out pid);
            if (pid == OwnPid) return false;
            // a maximised window also covers the whole screen when the taskbar is hidden: that's not fullscreen
            // (a real fullscreen video / game drops the caption; a maximised window keeps it)
            if (Native.IsZoomed(h) && (Native.GetWindowLong(h, -16) & 0x00C00000) == 0x00C00000) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(h, out r)) return false;
            int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
            return r.Left <= 0 && r.Top <= 0 && r.Right >= sw && r.Bottom >= sh;
        }
    }

    static class Icons
    {
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static IntPtr imageList = IntPtr.Zero;

        public static ImageSource ForPath(string path) { return ForPath(path, 96); }

        public static ImageSource ForPath(string path, int px)
        {
            if (string.IsNullOrEmpty(path)) return null;
            ImageSource img;
            string key = px + "|" + path;
            if (cache.TryGetValue(key, out img)) return img;
            img = Factory(path, px) ?? Shell(path);
            cache[key] = img;
            return img;
        }

        static Guid IID_Factory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
        static Guid IID_Item = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        /// Crisp, alpha-correct icon at the requested size (also works for shell:AppsFolder\ items).
        static ImageSource Factory(string path, int px)
        {
            object o = null;
            try
            {
                if (Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref IID_Factory, out o) != 0 || o == null) return null;
                var f = (Native.IShellItemImageFactory)o;
                IntPtr hbm;
                // ICONONLY(4) for files: we want the app icon, not a thumbnail of contents
                int flags = DockApp.IsShellPath(path) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path) ? 4 : 0;
                if (f.GetImage(new Native.SIZE { cx = px, cy = px }, flags, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                try { return FromHBitmap(hbm); }
                finally { Native.DeleteObject(hbm); }
            }
            catch { return null; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }

        static BitmapSource FromHBitmap(IntPtr hbm)
        {
            Native.DIBSECTION ds;
            if (Native.GetObject(hbm, Marshal.SizeOf(typeof(Native.DIBSECTION)), out ds) == 0) return null;
            var bm = ds.dsBm;
            if (bm.bmBits == IntPtr.Zero || bm.bmBitsPixel != 32) return null;
            int w = bm.bmWidth, h = bm.bmHeight, stride = bm.bmWidthBytes;
            var buf = new byte[stride * h];
            Marshal.Copy(bm.bmBits, buf, 0, buf.Length);
            if (ds.dsBmih.biHeight > 0)
            {
                var flip = new byte[buf.Length];
                for (int y = 0; y < h; y++) Buffer.BlockCopy(buf, y * stride, flip, (h - 1 - y) * stride, stride);
                buf = flip;
            }
            bool anyAlpha = false, straight = false;
            for (int i = 0; i < buf.Length; i += 4)
            {
                byte a = buf[i + 3];
                if (a != 0) anyAlpha = true;
                if (buf[i] > a || buf[i + 1] > a || buf[i + 2] > a) straight = true;
            }
            var fmt = !anyAlpha ? PixelFormats.Bgr32 : (straight ? PixelFormats.Bgra32 : PixelFormats.Pbgra32);
            var bs = BitmapSource.Create(w, h, 96, 96, fmt, null, buf, stride);
            bs.Freeze();
            return bs;
        }

        public static string DisplayName(string path)
        {
            object o = null;
            try
            {
                if (Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref IID_Item, out o) != 0 || o == null) return null;
                IntPtr p;
                if (((Native.IShellItem)o).GetDisplayName(0, out p) != 0) return null; // SIGDN_NORMALDISPLAY
                string s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                return s;
            }
            catch { return null; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }

        static ImageSource Shell(string path)
        {
            try
            {
                var fi = new Native.SHFILEINFO();
                IntPtr r = Native.SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf(fi), 0x4000); // SYSICONINDEX
                if (r == IntPtr.Zero) return null;
                if (imageList == IntPtr.Zero)
                {
                    Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                    Native.SHGetImageList(2, ref iid, out imageList); // SHIL_EXTRALARGE 48px
                }
                if (imageList == IntPtr.Zero) return null;
                IntPtr hi = Native.ImageList_GetIcon(imageList, fi.iIcon, 1);
                return FromHIcon(hi, true);
            }
            catch { return null; }
        }

        public static ImageSource ForWindow(IntPtr h)
        {
            IntPtr icon;
            Native.SendMessageTimeout(h, Native.WM_GETICON, (IntPtr)1, IntPtr.Zero, 2, 50, out icon);
            if (icon == IntPtr.Zero) icon = Native.GetClassLongPtr(h, -14);
            if (icon == IntPtr.Zero) Native.SendMessageTimeout(h, Native.WM_GETICON, (IntPtr)2, IntPtr.Zero, 2, 50, out icon);
            return FromHIcon(icon, false);
        }

        static ImageSource FromHIcon(IntPtr hi, bool destroy)
        {
            if (hi == IntPtr.Zero) return null;
            try
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(hi, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            catch { return null; }
            finally { if (destroy) Native.DestroyIcon(hi); }
        }

        public static ImageSource Letter(string name)
        {
            string ch = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            int hash = 0; foreach (char c in name ?? "") hash = hash * 31 + c;
            double hue = Math.Abs(hash % 360);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var g = new LinearGradientBrush(Hsv(hue, 0.55, 0.95), Hsv((hue + 40) % 360, 0.7, 0.75), 45);
                dc.DrawRoundedRectangle(g, null, new Rect(2, 2, 44, 44), 12, 12);
                var ft = new FormattedText(ch, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(Ui.TextFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 24, Brushes.White, 1.0);
                dc.DrawText(ft, new Point(24 - ft.Width / 2, 24 - ft.Height / 2));
            }
            var rtb = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            return rtb;
        }

        static Color Hsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }
    }

    /// Resolves .lnk targets via WScript.Shell (lazy COM).
    static class Shortcut
    {
        static object shell;
        public static string Target(string lnk)
        {
            try
            {
                if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return lnk;
                if (shell == null) shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic sc = ((dynamic)shell).CreateShortcut(lnk);
                string t = sc.TargetPath;
                if (string.IsNullOrEmpty(t))
                {
                    string ic = sc.IconLocation;
                    if (ic != null && ic.IndexOf("explorer.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    return null;
                }
                return t;
            }
            catch { return null; }
        }
    }
}
