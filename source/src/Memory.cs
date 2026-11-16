using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PrismDock
{
    /// Gives memory back to Windows: pages that were touched once (a panel, search, a preview) and are no longer used.
    /// Measured: the dock itself keeps ~20 MB in use; the rest of a 150 MB working set was stale pages.
    static class Memory
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PMC { public int cb, PageFaultCount; public IntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage; }
        [DllImport("psapi.dll")] static extern bool GetProcessMemoryInfo(IntPtr h, out PMC c, int size);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

        static DispatcherTimer soon, periodic;
        static DateTime lastTrim;

        public static void Start()
        {
            soon = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(6) };
            soon.Tick += (s, e) => { soon.Stop(); Trim("idle"); };
            periodic = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(60) };
            periodic.Tick += (s, e) => { if (WorkingSetMB > 70) Trim("periodic"); };
            periodic.Start();
            Soon();
        }

        /// something big was just closed: trim once things have settled
        public static void Soon() { if (soon == null) return; soon.Stop(); soon.Start(); }

        public static double WorkingSetMB
        {
            get { PMC c; return GetProcessMemoryInfo(GetCurrentProcess(), out c, Marshal.SizeOf(typeof(PMC))) ? c.WorkingSetSize.ToInt64() / 1048576.0 : 0; }
        }

        static void Trim(string why)
        {
            if ((DateTime.Now - lastTrim).TotalSeconds < 20) return;
            lastTrim = DateTime.Now;
            double before = WorkingSetMB;
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            Native.EmptyWorkingSet(GetCurrentProcess());
            if (Diag.On) EventLog.Write("trim (" + why + ") " + Math.Round(before) + " -> " + Math.Round(WorkingSetMB) + " MB");
        }
    }
}
