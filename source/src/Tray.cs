using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PrismDock
{
    class TrayIcon
    {
        public IntPtr Hwnd, HIcon;
        public uint Uid, Callback, Version, Pid;
        public string Text = "", Exe = "", Name = "";
        public bool Promoted;          // in the always-visible part of the Windows notification area
        public bool System;            // owned by explorer (network, volume, bluetooth ...): can't be closed
        public bool FromRegistry;      // Windows 11: known from NotifyIconSettings, no message channel to the icon
        public List<uint> AllPids = new List<uint>();
        public ImageSource Icon;
        public string Key { get { return Hwnd.ToInt64() + ":" + Uid; } }
    }

    /// Reads the notification-area icons out of the (hidden) Windows taskbar and forwards clicks to their apps
    /// exactly like the taskbar does (legacy and NOTIFYICON_VERSION_4 message formats).
    static class Tray
    {
        [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr a, IntPtr s, uint t, uint pr);
        [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr a, IntPtr s, uint t);
        [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr a, byte[] b, IntPtr s, out IntPtr r);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        [StructLayout(LayoutKind.Sequential)] struct NOTIFYICONDATA { public int cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon; }
        [DllImport("shell32.dll")] static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA d);

        static readonly Dictionary<long, ImageSource> iconCache = new Dictionary<long, ImageSource>();
        static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        static readonly object gate = new object();
        public static List<TrayIcon> Items = new List<TrayIcon>();
        public static event Action Changed;
        static string lastSig = "";
        static DispatcherTimer timer;
        static Dispatcher ui;
        static int busy;

        public static bool Supported
        {
            get { return (IntPtr.Size == 8 && Toolbar(false) != IntPtr.Zero) || RegistryPath() != null; }
        }

        /// Windows 11 keeps every notification icon it has seen under this key (exe path, pinned, icon snapshot)
        static string RegistryPath()
        {
            const string p = @"Control Panel\NotifyIconSettings";
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(p)) return k != null ? p : null; } catch { return null; }
        }

        public static void Start(Dispatcher d)
        {
            ui = d;
            if (timer != null) return;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) => Refresh();
            timer.Start();
            Refresh();
        }

        public static void Pause(bool p) { if (timer == null) return; if (p) timer.Stop(); else { timer.Start(); Refresh(); } }

        /// reads on a worker thread (explorer could be slow), publishes on the UI thread only when something changed
        public static void Refresh()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var list = new List<TrayIcon>();
                    IntPtr tb = Toolbar(false);
                    if (tb != IntPtr.Zero)
                    {
                        Read(tb, true, list);
                        Read(Toolbar(true), false, list);
                    }
                    else ReadRegistry(list);
                    string sig = string.Join("|", list.Select(i => i.Key + "/" + i.HIcon.ToInt64() + "/" + i.Text + "/" + i.Pid));
                    if (sig != lastSig)
                    {
                        lastSig = sig;
                        foreach (var i in list) if (i.Icon == null) i.Icon = IconFor(i.HIcon);
                        ui.BeginInvoke(new Action(() => { Items = list; if (Changed != null) Changed(); }));
                    }
                }
                catch { }
                finally { busy = 0; }
            });
        }

        static IntPtr Toolbar(bool overflow)
        {
            if (overflow)
                return Native.FindWindowEx(Native.FindWindow("NotifyIconOverflowWindow", null), IntPtr.Zero, "ToolbarWindow32", null);
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            IntPtr notify = Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            IntPtr pager = Native.FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
            return Native.FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null);
        }

        static IntPtr Send(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            IntPtr r;
            Native.SendMessageTimeout(h, msg, w, l, 2 /*ABORTIFHUNG*/, 300, out r);
            return r;
        }

        static void Read(IntPtr tb, bool promoted, List<TrayIcon> list)
        {
            if (tb == IntPtr.Zero) return;
            int n = (int)Send(tb, 0x418, IntPtr.Zero, IntPtr.Zero); // TB_BUTTONCOUNT
            if (n <= 0) return;
            uint pid; Native.GetWindowThreadProcessId(tb, out pid);
            IntPtr hp = Native.OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);
            if (hp == IntPtr.Zero) return;
            IntPtr mem = VirtualAllocEx(hp, IntPtr.Zero, (IntPtr)4096, 0x1000, 0x04);
            try
            {
                IntPtr r;
                var bb = new byte[32]; var td = new byte[32]; var txt = new byte[1024];
                for (int i = 0; i < n && i < 200; i++)
                {
                    if (Send(tb, 0x417, (IntPtr)i, mem) == IntPtr.Zero) continue; // TB_GETBUTTON
                    if (!ReadProcessMemory(hp, mem, bb, (IntPtr)32, out r)) continue;
                    int idCmd = BitConverter.ToInt32(bb, 4);
                    if ((bb[8] & 0x08) != 0) continue;  // TBSTATE_HIDDEN
                    long data = BitConverter.ToInt64(bb, 16);
                    if (data == 0 || !ReadProcessMemory(hp, (IntPtr)data, td, (IntPtr)32, out r)) continue;
                    var ic = new TrayIcon
                    {
                        Hwnd = (IntPtr)BitConverter.ToInt64(td, 0), Uid = BitConverter.ToUInt32(td, 8), Callback = BitConverter.ToUInt32(td, 12),
                        Version = BitConverter.ToUInt32(td, 20), HIcon = (IntPtr)BitConverter.ToInt64(td, 24), Promoted = promoted
                    };
                    int len = (int)Send(tb, 0x44B, (IntPtr)idCmd, mem + 512); // TB_GETBUTTONTEXTW
                    if (len > 0 && ReadProcessMemory(hp, mem + 512, txt, (IntPtr)Math.Min(1024, len * 2), out r))
                        ic.Text = Encoding.Unicode.GetString(txt, 0, Math.Min(1024, len * 2)).Trim();
                    if (!Native.IsWindow(ic.Hwnd)) { Forget(ic); continue; }       // app died without removing its icon
                    Native.GetWindowThreadProcessId(ic.Hwnd, out ic.Pid);
                    if (ic.Pid == ownPid) continue;
                    if (ic.Text == "PrismDock") { Forget(ic); continue; }          // leftovers of earlier PrismDock runs
                    string path = Native.GetProcessPath(ic.Pid) ?? "";
                    ic.Exe = path;
                    // Windows' own tray parts (network, volume, Security, Update ...) can't / shouldn't be ended
                    string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    ic.System = path.Length == 0 || path.StartsWith(win, StringComparison.OrdinalIgnoreCase);
                    bool shell = Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
                    string first = ic.Text.Split('\n')[0].Trim();
                    // title = the app's name; the tooltip goes underneath (explorer's own icons only have the tooltip)
                    ic.Name = shell || path.Length == 0 ? (first.Length > 0 ? first : "Windows") : DockApp.Describe(path);
                    // exe without a description ("wallpaper64"): its tooltip is the better name ("Wallpaper Engine")
                    if (!shell && path.Length > 0 && first.Length > 0 && first.Length <= 40 &&
                        ic.Name.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)) ic.Name = first;
                    if (list.Any(x => x.Key == ic.Key)) continue;
                    list.Add(ic);
                }
            }
            finally { VirtualFreeEx(hp, mem, IntPtr.Zero, 0x8000); Native.CloseHandle(hp); }
        }

        // ------------------------------------------------------------ Windows 11
        static readonly Dictionary<string, ImageSource> regIcons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

        /// "{F38BF404-...}\\notepad.exe" -> "C:\\Windows\\notepad.exe"
        static string ResolveKnownFolder(string p)
        {
            if (string.IsNullOrEmpty(p) || p[0] != '{') return p;
            int end = p.IndexOf('}');
            Guid g;
            if (end < 0 || !Guid.TryParse(p.Substring(0, end + 1), out g)) return p;
            IntPtr s;
            if (SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out s) != 0) return p;
            try { return Marshal.PtrToStringUni(s) + p.Substring(end + 1); } finally { Marshal.FreeCoTaskMem(s); }
        }

        /// running apps that own a notification icon: registry entries whose exe is currently running
        static void ReadRegistry(List<TrayIcon> list)
        {
            string key = RegistryPath();
            if (key == null) return;
            // exe path -> pids (one pass over the processes)
            var running = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pr in Process.GetProcesses())
            {
                try
                {
                    uint pid = (uint)pr.Id;
                    if (pid == ownPid || pid <= 4) continue;
                    string path = Native.GetProcessPath(pid);
                    if (string.IsNullOrEmpty(path)) continue;
                    List<uint> l;
                    if (!running.TryGetValue(path, out l)) running[path] = l = new List<uint>();
                    l.Add(pid);
                }
                catch { }
                finally { pr.Dispose(); }
            }
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            using (var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key))
            {
                if (root == null) return;
                foreach (var name in root.GetSubKeyNames())
                {
                    try
                    {
                        using (var k = root.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            string exe = ResolveKnownFolder(k.GetValue("ExecutablePath") as string);
                            List<uint> pids;
                            if (string.IsNullOrEmpty(exe) || !running.TryGetValue(exe, out pids)) continue;
                            if (list.Any(x => x.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase))) continue;   // one row per app
                            string tip = (k.GetValue("InitialTooltip") as string ?? "").Trim();
                            object prom = k.GetValue("IsPromoted");
                            var ic = new TrayIcon
                            {
                                FromRegistry = true, Exe = exe, Pid = pids[0], AllPids = pids, Text = tip,
                                Promoted = prom is int && (int)prom != 0,
                                System = exe.StartsWith(win, StringComparison.OrdinalIgnoreCase),
                                Hwnd = IntPtr.Zero, Uid = (uint)name.GetHashCode()
                            };
                            ic.Name = DockApp.Describe(exe);
                            string first = tip.Split('\n')[0].Trim();
                            if (first.Length > 0 && first.Length <= 40 && ic.Name.Equals(Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase)) ic.Name = first;
                            ic.Icon = RegIcon(exe, k.GetValue("IconSnapShot") as byte[]);
                            list.Add(ic);
                        }
                    }
                    catch { }
                }
            }
        }

        static ImageSource RegIcon(string exe, byte[] snapshot)
        {
            ImageSource img;
            lock (gate) { if (regIcons.TryGetValue(exe, out img)) return img; }
            try
            {
                if (snapshot != null && snapshot.Length > 0)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(snapshot); bi.EndInit();
                    bi.Freeze(); img = bi;
                }
            }
            catch { img = null; }
            if (img == null)
            {
                try
                {
                    using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze(); img = bs;
                    }
                }
                catch { img = null; }
            }
            lock (gate) { regIcons[exe] = img; }
            return img;
        }

        /// no message channel to the icon (Windows 11): bring the app's own window back, or ask the app to show itself
        static void OpenApp(TrayIcon i)
        {
            IntPtr best = IntPtr.Zero;
            var pids = new HashSet<uint>(i.AllPids);
            Native.EnumWindows((h, l) =>
            {
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains(pid) || Native.GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;
                long ex = (uint)Native.GetWindowLong(h, -20);
                if ((ex & 0x80) != 0) return true;            // WS_EX_TOOLWINDOW
                long st = (uint)Native.GetWindowLong(h, -16);
                if ((st & 0x00C00000) != 0x00C00000) return true; // needs a caption: skips message-only / helper windows
                best = h; return false;
            }, IntPtr.Zero);
            if (best != IntPtr.Zero)
            {
                Native.ShowWindow(best, Native.IsIconic(best) ? 9 /*SW_RESTORE*/ : 5 /*SW_SHOW*/);
                Native.ForceForeground(best);
                return;
            }
            // most tray apps are single-instance: starting them again shows their window (Discord, Steam, Spotify ...)
            try { Process.Start(new ProcessStartInfo(i.Exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(i.Exe) }); } catch { }
        }

        static void Forget(TrayIcon ic)
        {
            if (ic.FromRegistry) return;
            try
            {
                var d = new NOTIFYICONDATA { cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)), hWnd = ic.Hwnd, uID = ic.Uid };
                Shell_NotifyIcon(2 /*NIM_DELETE*/, ref d);
            }
            catch { }
        }

        static ImageSource IconFor(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            ImageSource img;
            lock (gate) { if (iconCache.TryGetValue(h.ToInt64(), out img)) return img; }
            try
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); // icon stays explorer's: not destroyed
                bs.Freeze(); img = bs;
            }
            catch { img = null; }
            lock (gate) { if (iconCache.Count > 300) iconCache.Clear(); iconCache[h.ToInt64()] = img; }
            return img;
        }

        // ------------------------------------------------------------ input forwarding
        const uint WM_CONTEXTMENU = 0x7B, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, NIN_SELECT = 0x400;

        static void Post(TrayIcon i, uint ev, int x, int y)
        {
            IntPtr w, l;
            if (i.Version >= 4) { w = (IntPtr)((y << 16) | (x & 0xFFFF)); l = (IntPtr)(((int)i.Uid << 16) | (int)ev); }
            else { w = (IntPtr)(int)i.Uid; l = (IntPtr)(int)ev; }
            Native.PostMessage(i.Hwnd, i.Callback, w, l);
        }

        /// screen point (px) is the anchor for version-4 apps (where they place flyouts / menus)
        public static void Click(TrayIcon i, Point anchorPx, bool right, bool dbl)
        {
            if (i.FromRegistry) { AllowSetForegroundWindow(i.Pid); OpenApp(i); return; }
            if (!Native.IsWindow(i.Hwnd)) { Refresh(); return; }
            AllowSetForegroundWindow(i.Pid);   // lets the app bring its window / menu to the front
            int x = (int)anchorPx.X, y = (int)anchorPx.Y;
            if (right)
            {
                Post(i, WM_RBUTTONDOWN, x, y); Post(i, WM_RBUTTONUP, x, y);
                if (i.Version >= 3) Post(i, WM_CONTEXTMENU, x, y);
            }
            else if (dbl)
            {
                Post(i, WM_LBUTTONDBLCLK, x, y); Post(i, WM_LBUTTONUP, x, y);
            }
            else
            {
                Post(i, WM_LBUTTONDOWN, x, y); Post(i, WM_LBUTTONUP, x, y);
                if (i.Version >= 3) Post(i, NIN_SELECT, x, y);
            }
        }

        /// ends the app behind a tray icon (whole process tree, like "End task" in Task Manager)
        public static bool Close(TrayIcon i)
        {
            if (i.System || i.Pid == 0 || i.Pid == ownPid) return false;
            try
            {
                var pids = i.AllPids.Count > 0 ? i.AllPids : new List<uint> { i.Pid };
                var psi = new ProcessStartInfo("taskkill.exe", string.Join(" ", pids.Select(p => "/PID " + p)) + " /T /F") { CreateNoWindow = true, UseShellExecute = false };
                using (var p = Process.Start(psi)) p.WaitForExit(4000);
            }
            catch { try { using (var p = Process.GetProcessById((int)i.Pid)) p.Kill(); } catch { return false; } }
            Forget(i);
            Refresh();
            return true;
        }
    }

    /// "Running in the background": every tray app with open (left click), its own menu (right click) and close.
    class TrayPanel : PanelWindow
    {
        StackPanel list; TextBlock count;
        public TrayPanel() : base(400, 520)
        {
            Action h = () => { if (IsOpen) Fill(); };
            Tray.Changed += h;
            OnDestroy(() => Tray.Changed -= h);
        }

        protected override UIElement Build()
        {
            var t = Theme.Cur;
            var g = new Grid { Margin = new Thickness(18, 16, 18, 14) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new Grid();
            head.Children.Add(Header("Arka planda çalışanlar"));
            count = Ui.Text("", 12, FontWeights.Normal, Ui.Br(t.Sub)); count.HorizontalAlignment = HorizontalAlignment.Right; count.VerticalAlignment = VerticalAlignment.Top; count.Margin = new Thickness(0, 4, 0, 0);
            head.Children.Add(count);
            g.Children.Add(head);
            list = new StackPanel();
            var sv = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
            Grid.SetRow(sv, 1); g.Children.Add(sv);
            var foot = Ui.Text(Tray.Items.Any(x => x.FromRegistry) ? "Tıkla: aç  ·  ✕: kapat" : "Tıkla: aç  ·  Sağ tık: uygulamanın menüsü  ·  ✕: kapat", 11, FontWeights.Normal, Ui.Br(t.Sub));
            foot.Margin = new Thickness(2, 10, 0, 0); foot.Opacity = 0.85;
            Grid.SetRow(foot, 2); g.Children.Add(foot);
            Fill();
            return g;
        }

        protected override void Opened() { Tray.Refresh(); Fill(); }

        void Fill()
        {
            if (list == null) return;
            list.Children.Clear();
            var items = Tray.Items.OrderBy(i => i.System).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            bool sysHeader = false;
            foreach (var i in items)
            {
                if (i.System && !sysHeader) { sysHeader = true; var h = Label("SİSTEM"); list.Children.Add(h); }
                list.Children.Add(Row(i));
            }
            count.Text = items.Count(i => !i.System) + " uygulama";
            if (items.Count == 0) list.Children.Add(Ui.Text(Tray.Supported ? "Arka planda çalışan uygulama yok" : "Bu Windows sürümünde bildirim alanı okunamıyor", 13, FontWeights.Normal, Ui.Br(Theme.Cur.Sub)));
        }

        FrameworkElement Row(TrayIcon i)
        {
            var t = Theme.Cur;
            var g = new Grid { Height = 48 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var img = new Image { Width = 22, Height = 22, Source = i.Icon };
            if (i.Icon == null) { string exe = i.Exe; Icons.Async(Dispatcher, () => Icons.ForPath(exe, 32), x => img.Source = x); }
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            g.Children.Add(img);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var n = Ui.Text(i.Name, 13.5, FontWeights.SemiBold, Ui.Br(t.Fg)); n.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(n);
            var lines = i.Text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && x != i.Name).ToList();
            string sub = lines.Count > 0 ? string.Join(" · ", lines) : Path.GetFileName(i.Exe);
            if (sub.Length > 0) { var s = Ui.Text(sub, 11, FontWeights.Normal, Ui.Br(t.Sub)); s.TextTrimming = TextTrimming.CharacterEllipsis; texts.Children.Add(s); }
            Grid.SetColumn(texts, 1); g.Children.Add(texts);
            Border row = null;
            row = Ui.Button(g, () => { var p = Anchor(row); CloseAnimated(); Tray.Click(i, p, false, false); }, null, 0, 0, 12);
            row.Margin = new Thickness(0, 1, 0, 1);
            row.MouseRightButtonUp += (s, e) => { var p = Anchor(row); CloseAnimated(); Tray.Click(i, p, true, false); e.Handled = true; };
            if (!i.System)
            {
                var x = Ui.Glyph("", 11, Ui.Br(t.Sub));
                TextBlock xt = x;
                bool armed = false;
                Border xb = null;
                xb = Ui.Button(x, () =>
                {
                    if (!armed)
                    {
                        armed = true;
                        xt.Text = "Kapat?"; xt.FontFamily = Ui.TextFont; xt.FontSize = 11.5; xt.Foreground = Ui.Br(Color.FromRgb(255, 110, 100));
                        xb.Width = double.NaN; xb.Padding = new Thickness(10, 0, 10, 0);
                        var tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                        tm.Tick += (s, e) => { tm.Stop(); if (!armed) return; armed = false; xt.Text = ""; xt.FontFamily = Ui.IconFont; xt.FontSize = 11; xt.Foreground = Ui.Br(t.Sub); xb.Width = 30; xb.Padding = new Thickness(0); };
                        tm.Start();
                        return;
                    }
                    Tray.Close(i);
                    row.IsEnabled = false; row.Opacity = 0.35;
                }, "Uygulamayı kapat", 30, 30, 15);
                xb.Margin = new Thickness(4, 0, 6, 0);
                Grid.SetColumn(xb, 2); g.Children.Add(xb);
            }
            return row;
        }

        Point Anchor(FrameworkElement el)
        {
            try { return el.PointToScreen(new Point(22, el.ActualHeight / 2)); } catch { return new Point(0, 0); }
        }
    }
}
