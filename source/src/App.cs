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
        public static DockWindow Dock;
        public static IslandWindow Island;
        static Mutex mutex;
        static System.Windows.Forms.NotifyIcon tray;
        static bool cleaned;

        [STAThread]
        static void Main(string[] args)
        {
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

            int si = Array.FindIndex(args, a => a == "--snapshot");
            if (si >= 0 && si + 1 < args.Length)
            {
                string dir = args[si + 1]; Directory.CreateDirectory(dir);
                Wallpaper.Load();
                new Application();
                AppCatalog.Load(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                var sw0 = DateTime.Now;
                while (!AppCatalog.Ready && (DateTime.Now - sw0).TotalSeconds < 15)
                {
                    var fr = new System.Windows.Threading.DispatcherFrame();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => fr.Continue = false));
                    System.Windows.Threading.Dispatcher.PushFrame(fr); Thread.Sleep(30);
                }
                foreach (var th in Theme.All.Where(x => x.Id == "liquid" || (x.Id == "frost" && Environment.GetEnvironmentVariable("PRISM_SNAPQ") == null)))
                {
                    Theme.Cur = th;
                    foreach (var q in (Environment.GetEnvironmentVariable("PRISM_SNAPQ") ?? "rocket").Split('|'))
                    {
                        var sw = new SearchWindow(); string qq = q;
                        sw.Snapshot(Path.Combine(dir, th.Id + "_search_" + Search.Fold(q).Replace(' ', '_') + ".png"), () => sw.DebugQuery(qq));
                    }
                    var se = new SearchWindow(); se.Snapshot(Path.Combine(dir, th.Id + "_search_empty.png"), () => { });
                    var pk = new AppPickerPanel(); pk.Snapshot(Path.Combine(dir, th.Id + "_picker.png"), () => { });
                    new StartPanel().Snapshot(Path.Combine(dir, th.Id + "_start.png"));
                    new TrayPanel().Snapshot(Path.Combine(dir, th.Id + "_tray.png"), () => Tray.Start(System.Windows.Threading.Dispatcher.CurrentDispatcher));
                }
                foreach (var th in Theme.All.Where(x => x.Id == "liquid" || x.Id == "frost" || x.Id == "aurora"))
                {
                    Theme.Cur = th;
                    new CalendarPanel().Snapshot(Path.Combine(dir, th.Id + "_calendar.png"));
                    new SystemPanel().Snapshot(Path.Combine(dir, th.Id + "_system.png"));
                    new PomodoroPanel().Snapshot(Path.Combine(dir, th.Id + "_pomodoro.png"));
                    new SettingsPanel().Snapshot(Path.Combine(dir, th.Id + "_settings.png"));
                    new NotesPanel().Snapshot(Path.Combine(dir, th.Id + "_notes.png"));
                    new KeyboardPanel().Snapshot(Path.Combine(dir, th.Id + "_keyboard.png"));
                }
                return;
            }

            bool created;
            mutex = new Mutex(true, "PrismDock_SingleInstance_v1", out created);
            if (!created) return;
            S.AutoStart = S.AutoStart || AutoStart.RunKeyExists();   // exact state is checked in the background below

            var startClock = System.Diagnostics.Stopwatch.StartNew();
            Theme.Cur = Theme.Get(S.Theme);
            InitPins();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category != Microsoft.Win32.UserPreferenceCategory.Desktop || Dock == null) return;
                Dock.Dispatcher.BeginInvoke(new Action(() => { if (!S.LiveGlass) Wallpaper.Load(); Dock.Rebuild(); Dock.RethemePanels(); }));
            };

            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log(e.ExceptionObject as Exception); Cleanup(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();
            app.DispatcherUnhandledException += (s, e) => { Log(e.Exception); e.Handled = true; };
            app.SessionEnding += (s, e) => { sessionEnding = true; Cleanup(); };

            if (!S.HideTaskbar && S.TaskbarTouched) Taskbar.Restore(S);

            Dock = new DockWindow();
            Dock.Show();
            if (S.HideTaskbar) Taskbar.HideHard(S);   // off the UI thread, after the dock is on screen
            EventLog.Write("start  build " + EventLog.WindowsBuild + "  dock shown in " + startClock.ElapsedMilliseconds + " ms" +
                "  (" + (int)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds + " ms since process start, uptime " + (Native.GetTickCount64() / 1000) + " s)");
            string selfExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                bool on = AutoStart.Migrate(selfExe);
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
                var th = new Thread(() => { ev.WaitOne(); Dock.Dispatcher.BeginInvoke(new Action(Quit)); }) { IsBackground = true };
                th.Start();
            }
            catch { }
        }


        static void SyncIsland()
        {
            if (Environment.GetEnvironmentVariable("PRISM_DRAGTEST") != null) return;
            if (S.Island && Island == null) { Island = new IslandWindow(); Island.Show(); }
            else if (!S.Island && Island != null) { Island.Release(); Island.Close(); Island = null; }
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
            Dock.Rebuild();
            if (themeChanged) Dock.RethemePanels();
            SyncIsland();
            if (Island != null && themeChanged) Island.Retheme();
            if (!S.Island || !S.AutoHide) Dock.ShowDock();
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
        static void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            try { if (Island != null) Island.Release(); } catch { }
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
