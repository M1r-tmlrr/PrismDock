using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PrismDock
{
    class App : Application
    {
        public static Settings S;
        public static DockWindow Dock;                                   // primary monitor (owns hooks, island, taskbar)
        public static readonly System.Collections.Generic.List<DockWindow> Docks = new System.Collections.Generic.List<DockWindow>();
        public static IslandWindow Island;                                // primary monitor
        public static readonly System.Collections.Generic.List<IslandWindow> Islands = new System.Collections.Generic.List<IslandWindow>();
        static Mutex mutex;
        static System.Windows.Forms.NotifyIcon tray;
        static bool cleaned;

        [STAThread]
        static void Main(string[] args)
        {
            // the Windows-key hook runs in its own tiny process (see KeyHook.cs); nothing else is loaded there
            if (args.Length >= 3 && args[0] == "--keyhook") { WinKeyHook.RunHelper(args); return; }
            // multi-core JIT: the first run records what gets compiled at startup, later runs compile it in parallel
            try { System.Runtime.ProfileOptimization.SetProfileRoot(Settings.Dir); System.Runtime.ProfileOptimization.StartProfile("startup.jit"); } catch { }
            S = Settings.Load();
            if (args.Any(a => a.Equals("--quit", StringComparison.OrdinalIgnoreCase)))
            {
                // asks a running instance to exit cleanly (used by the installer / uninstaller)
                try { EventWaitHandle ev; if (EventWaitHandle.TryOpenExisting(QuitEventName, out ev)) { ev.Set(); ev.Dispose(); } } catch { }
                return;
            }
            if (args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase)))
            {
                S.TaskbarTouched = true;
                Taskbar.Restore(S);
                return;
            }

            bool created;
            mutex = new Mutex(true, "PrismDock_SingleInstance_v1", out created);
            if (!created)
            {
                // starting PrismDock again replaces the running one (also when it is stuck and can't be closed)
                try { EventWaitHandle qe; if (EventWaitHandle.TryOpenExisting(QuitEventName, out qe)) { qe.Set(); qe.Dispose(); } } catch { }
                try { if (!mutex.WaitOne(8000)) return; }
                catch (AbandonedMutexException) { }
            }
            Watchdog.Start();
            S.AutoStart = S.AutoStart || AutoStart.RunKeyExists();   // exact state is checked in the background below

            var startClock = System.Diagnostics.Stopwatch.StartNew();
            Theme.Cur = Theme.Get(S.Theme);
            InitPins();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category != Microsoft.Win32.UserPreferenceCategory.Desktop || Dock == null) return;
                Dock.Dispatcher.BeginInvoke(new Action(() => { if (!S.LiveGlass || LiveCapture.TooSlow) Wallpaper.Load(); Dock.Rebuild(); Dock.RethemePanels(); }));
            };
            // weak GPU: screen copies too slow -> same look from the blurred wallpaper, no live capture
            LiveCapture.SlowChanged += () =>
            {
                Wallpaper.Load();
                foreach (var d in Docks.ToList()) d.Rebuild();
                if (Dock != null) Dock.RethemePanels();
                foreach (var i in Islands.ToList()) i.Retheme();
            };

            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log(e.ExceptionObject as Exception); Cleanup(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();
            app.DispatcherUnhandledException += (s, e) => { Log(e.Exception); e.Handled = true; };
            app.SessionEnding += (s, e) => { sessionEnding = true; Cleanup(); };

            if (!S.HideTaskbar && S.TaskbarTouched) Taskbar.Restore(S);

            Dock = new DockWindow();
            Dock.Show();
            SyncMonitors();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => Dock.Dispatcher.BeginInvoke(new Action(SyncMonitors), System.Windows.Threading.DispatcherPriority.Background);
            if (S.HideTaskbar) Taskbar.HideHard(S);   // off the UI thread, after the dock is on screen
            EventLog.Write("start  build " + EventLog.WindowsBuild + "  dock shown in " + startClock.ElapsedMilliseconds + " ms" +
                "  (" + (int)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds + " ms since process start, uptime " + (Native.GetTickCount64() / 1000) + " s)");
            string selfExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            bool wantAutoStart = S.AutoStart;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                bool on = AutoStart.Migrate(selfExe, wantAutoStart);
                autoApplied = on;
                Dock.Dispatcher.BeginInvoke(new Action(() => { if (S.AutoStart != on) { S.AutoStart = on; S.Save(); } }));
            });
            SyncIsland();
            SetupTray();
            ListenForQuit();

            // one-time trim after startup: drops init-only pages from the working set
            Memory.Start();

            app.Run();
        }

        static void InitPins()
        {
            if (S.PinsInit) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                if (Directory.Exists(dir))
                    foreach (var f in new DirectoryInfo(dir).GetFiles("*.lnk").OrderBy(f => f.CreationTime))
                        S.Pinned.Add(f.FullName);
            }
            catch { }
            S.PinsInit = true;
            S.Save();
        }

        const string QuitEventName = "PrismDock_Quit_v1";
        static void ListenForQuit()
        {
            try
            {
                var ev = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
                var th = new Thread(() =>
                {
                    ev.WaitOne();
                    Dock.Dispatcher.BeginInvoke(new Action(Quit));
                    // the UI thread may be stuck: give it 3 s, then clean up from here and leave anyway
                    Thread.Sleep(3000);
                    EventLog.Write("quit: UI did not respond, forced exit");
                    EmergencyExit(0);
                }) { IsBackground = true };
                th.Start();
            }
            catch { }
        }


        /// a dock on every monitor (when enabled); monitors that appear / disappear get / lose theirs
        public static void SyncMonitors()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            // other monitors get only the island (no dock); the dock stays on the primary monitor
            foreach (var d in Docks.ToList()) if (!d.Primary) d.Close();
            bool want = S.Island && S.AllMonitors;
            foreach (var i in Islands.ToList())
                if (!i.Primary && (!want || !screens.Any(x => x.DeviceName == i.Device))) { i.Release(); i.Close(); Islands.Remove(i); }
            if (want)
                foreach (var scr in screens)
                    if (!scr.Primary && !Islands.Any(i => i.Device == scr.DeviceName)) { var isl = new IslandWindow(scr); Islands.Add(isl); isl.Show(); }
            foreach (var d in Docks.ToList()) d.Relayout();
        }

        public static void PinsChanged(DockWindow from)
        {
            foreach (var d in Docks.ToList()) if (d != from) d.ReloadPins();
        }

        /// the dock on the monitor under the mouse (Win key / Alt+Space open there)
        public static DockWindow DockAtCursor()
        {
            string dev = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).DeviceName;
            return Docks.FirstOrDefault(d => d.Device == dev) ?? Dock;
        }

        static void SyncIsland()
        {
            if (S.Island && Island == null) { Island = new IslandWindow(); Islands.Add(Island); Island.Show(); }
            else if (!S.Island && Island != null) { Island.Release(); Island.Close(); Islands.Remove(Island); Island = null; }
        }

        static string lastTheme; static bool lastLive = true; static int lastLight = -1, lastRefr = -1;
        public static void ApplySettings()
        {
            S.Save();
            bool themeChanged = lastTheme != null ? lastTheme != S.Theme : Theme.Cur.Id != S.Theme;
            if (lastLive != S.LiveGlass) themeChanged = true;
            if (lastLight >= 0 && (lastLight != S.GlassLight || lastRefr != S.Refraction)) themeChanged = true;
            lastLight = S.GlassLight; lastRefr = S.Refraction;
            lastLive = S.LiveGlass;
            lastTheme = S.Theme;
            Theme.Cur = Theme.Get(S.Theme);
            SetAutoStart(S.AutoStart);
            if (S.HideTaskbar) Taskbar.HideHard(S); else Taskbar.Restore(S);
            foreach (var d in Docks.ToList()) d.Rebuild();
            if (themeChanged) Dock.RethemePanels();
            SyncIsland();
            SyncMonitors();
            if (themeChanged) foreach (var i in Islands.ToList()) i.Retheme();
            if (!S.Island || !S.AutoHide) foreach (var d in Docks.ToList()) d.ShowDock();
        }

        static bool? autoApplied;
        static void SetAutoStart(bool on)
        {
            if (autoApplied == on) return;
            autoApplied = on;
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => AutoStart.Set(on, exe));
        }

        static void SetupTray()
        {
            try
            {
                var bmp = new System.Drawing.Bitmap(32, 32);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0, 0, 32, 32),
                        System.Drawing.Color.FromArgb(94, 242, 208), System.Drawing.Color.FromArgb(140, 90, 255), 45f))
                        g.FillEllipse(br, 2, 2, 28, 28);
                    using (var w = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(170, 255, 255, 255)))
                        g.FillEllipse(w, 8, 5, 16, 9);
                }
                tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.Icon.FromHandle(bmp.GetHicon()), Text = "PrismDock", Visible = true };
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Ayarlar", null, (s, e) => Dock.OpenSettings());
                menu.Items.Add("Windows görev çubuğunu geri getir", null, (s, e) => { S.HideTaskbar = false; ApplySettings(); });
                menu.Items.Add("Çıkış", null, (s, e) => Quit());
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += (s, e) => Dock.OpenSettings();
            }
            catch { }
        }

        public static void Quit()
        {
            Cleanup();
            Current.Shutdown();
        }

        static bool sessionEnding;

        /// last-resort exit from a background thread: release the screen strips, give the taskbar back, go
        public static void EmergencyExit(int code)
        {
            try { Taskbar.UnreserveAll(); } catch { }
            try { if (S != null) Taskbar.Restore(S); } catch { }
            try { if (tray != null) tray.Visible = false; } catch { }
            Environment.Exit(code);
        }
        static void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            try { foreach (var i in Islands.ToList()) i.Release(); } catch { }
            try { if (Dock != null) Dock.Shutdown(); } catch { }
            // on shutdown / sign-out keep the Windows taskbar hidden (auto-hide), so at the next login
            // PrismDock appears instead of the Windows taskbar. A normal quit restores it.
            if (!sessionEnding) { try { Taskbar.Restore(S); } catch { } }
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } } catch { }
            try { S.Save(); } catch { }
        }

        static void Log(Exception ex)
        {
            if (ex == null) return;
            try { File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), DateTime.Now + "  " + ex + "\r\n\r\n"); } catch { }
        }
    }
}
