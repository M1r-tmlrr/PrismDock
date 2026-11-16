using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;

namespace PrismDock
{
    /// Event counters, only active when PRISM_DEBUG is set.
    static class Diag
    {
        public static readonly bool On = Environment.GetEnvironmentVariable("PRISM_DEBUG") != null;
        static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        static DispatcherTimer t;
        public static void Hit(string k)
        {
            if (!On) return;
            int c; counts.TryGetValue(k, out c); counts[k] = c + 1;
            if (t == null)
            {
                t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                t.Tick += (s, e) =>
                {
                    var line = DateTime.Now.ToString("HH:mm:ss") + " ";
                    foreach (var kv in counts) line += kv.Key + "=" + kv.Value + " ";
                    counts.Clear();
                    try { File.AppendAllText(Path.Combine(Settings.Dir, "diag.log"), line + Environment.NewLine); } catch { }
                };
                t.Start();
            }
        }
    }
}
