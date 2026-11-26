using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;

namespace PrismDock
{
    /// Small always-on log of state changes (fullscreen, taskbar, startup) in %AppData%\PrismDock\events.log.
    /// Capped at 256 KB, no personal data beyond window class / process names.
    static class EventLog
    {
        static readonly object gate = new object();
        public static void Write(string msg)
        {
            try
            {
                lock (gate)
                {
                    string p = Path.Combine(Settings.Dir, "events.log");
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 256 * 1024) File.Delete(p);
                    File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }

        public static string Describe(IntPtr h)
        {
            if (h == IntPtr.Zero) return "(none)";
            uint pid; Native.GetWindowThreadProcessId(h, out pid);
            string exe = System.IO.Path.GetFileName(Native.GetProcessPath(pid) ?? "?");
            return exe + " [" + Native.GetClass(h) + "]";
        }

        public static int WindowsBuild
        {
            get
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    { int b; return k != null && int.TryParse(k.GetValue("CurrentBuildNumber") as string, out b) ? b : 0; }
                }
                catch { return 0; }
            }
        }
    }

    /// Event counters, only active when PRISM_DEBUG is set.
    static class Diag
    {
        public static readonly bool On = Environment.GetEnvironmentVariable("PRISM_DEBUG") != null;
        static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        static DispatcherTimer t;

        public static void Hit(string k)
        {
            if (!On) return;
            lock (counts) { int c; counts.TryGetValue(k, out c); counts[k] = c + 1; }
            if (t == null)
            {
                t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                t.Tick += (s, e) =>
                {
                    var line = DateTime.Now.ToString("HH:mm:ss") + " ";
                    lock (counts) { foreach (var kv in counts) line += kv.Key + "=" + kv.Value + " "; counts.Clear(); }
                    try { File.AppendAllText(Path.Combine(Settings.Dir, "diag.log"), line + Environment.NewLine); } catch { }
                };
                t.Start();
            }
        }
    }
}
