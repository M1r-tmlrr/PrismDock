using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;

namespace PrismDock
{
    /// Notices a frozen UI thread. After 12 s without a heartbeat the dock logs it, releases its screen strips
    /// and restarts itself, so the user is never left with a dead dock that can't be closed.
    static class Watchdog
    {
        static long beat;
        static volatile bool armed;   // only after the UI thread has answered once (start-up can be slow at sign-in)
        static Dispatcher ui;
        public static volatile string Op = "";   // breadcrumb: what the UI thread was doing

        public static void Start()
        {
            ui = Dispatcher.CurrentDispatcher;
            beat = Environment.TickCount;
            var t = new Thread(Loop) { IsBackground = true, Name = "PrismDock watchdog", Priority = ThreadPriority.AboveNormal };
            t.Start();
        }

        static void Loop()
        {
            int last = Environment.TickCount;
            while (true)
            {
                Thread.Sleep(1000);
                int now = Environment.TickCount;
                bool resumed = now - last > 4000;   // machine slept / process was paused: not a hang
                last = now;
                if (resumed) { Interlocked.Exchange(ref beat, now); continue; }
                ui.BeginInvoke(DispatcherPriority.Send, new Action(() => { Interlocked.Exchange(ref beat, Environment.TickCount); armed = true; }));
                if (!armed) { Interlocked.Exchange(ref beat, now); continue; }
                long since = now - Interlocked.Read(ref beat);
                if (since < 12000) continue;
                EventLog.Write("UI frozen for " + since / 1000 + " s (last: " + Op + ") -> restarting");
                Restart();
                return;
            }
        }

        static void Restart()
        {
            try
            {
                // through the logon task when there is one: a clean start, same as at sign-in
                var psi = new ProcessStartInfo("schtasks.exe", "/run /tn \"PrismDock Autostart\"") { CreateNoWindow = true, UseShellExecute = false };
                bool viaTask = false;
                using (var p = Process.Start(psi)) { p.WaitForExit(5000); viaTask = p.ExitCode == 0; }
                if (!viaTask) Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location) { UseShellExecute = false });
            }
            catch { }
            App.EmergencyExit(3);
        }
    }
}
